extern alias ClientLib;
extern alias MasterLib;
extern alias SlaveLib; // Добавили алиас для Слейва

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Newtonsoft.Json;

// --- Алиасы Типов ---

// Master
using MasterProcessor = MasterLib::MasterApp.core.TextProcessor;
using MasterSlaveManager = MasterLib::MasterApp.core.SlaveManager;
using MasterListener = MasterLib::MasterApp.core.MasterListener;
using MasterClientHandler = MasterLib::MasterApp.core.ClientHandler;
using MasterFinalResult = MasterLib::SharedModels.FinalResult;
using MasterTaskMessage = MasterLib::SharedModels.TaskMessage;
using MasterResultMessage = MasterLib::SharedModels.ResultMessage;

// Client
using ClientMasterClient = ClientLib::ClientApp.Core.MasterClient;
using ClientRequest = ClientLib::SharedModels.ClientRequest;
using ClientFinalResult = ClientLib::SharedModels.FinalResult;

// Slave
using SlaveConnector = SlaveLib::SlaveApp.Core.MasterConnector;
// Нам не обязательно импортировать модели Слейва, мы будем общаться с ним через JSON

namespace App.Tests
{
    public class TextProcessingTests
    {
        [Fact]
        public async Task Master_DistributeAndCollectAsync_NoSlaves_CalculatesCorrectly()
        {
            Action<string> logger = (msg) => { };
            var slaveManager = new MasterSlaveManager(logger);
            var processor = new MasterProcessor(slaveManager, logger);

            string inputText = "Hello world! Hello user.";

            MasterFinalResult result = await processor.DistributeAndCollectAsync(inputText);

            Assert.NotNull(result);
            Assert.Equal(2, result.Frequencies["hello"]);
            Assert.Equal(1, result.Frequencies["world"]);
            Assert.False(result.Frequencies.ContainsKey("!"));
        }

        // НОВЫЙ ТЕСТ: Проверка пустого ввода
        [Fact]
        public async Task Master_DistributeAndCollectAsync_EmptyInput_ReturnsEmpty()
        {
            Action<string> logger = (msg) => { };
            var slaveManager = new MasterSlaveManager(logger);
            var processor = new MasterProcessor(slaveManager, logger);

            MasterFinalResult result = await processor.DistributeAndCollectAsync("");

            Assert.NotNull(result);
            Assert.Empty(result.Frequencies);
            Assert.True(result.TotalProcessingMs >= 0);
        }
    }

    public class SlaveTests
    {
        // НОВЫЙ ТЕСТ: Проверяем логику Слейва изолированно.
        // Тест притворяется Мастером (Fake Master), отправляет задачу Слейву и проверяет ответ.
        [Fact]
        public async Task Slave_ConnectsAndProcesses_TaskCorrectly()
        {
            int port = TestUtils.GetFreeTcpPort();
            string host = "127.0.0.1";
            Action<string> logger = (s) => { };

            var cts = new CancellationTokenSource();

            // 1. Запускаем "Фейковый Мастер" (обычный TcpListener)
            var fakeMasterListener = new TcpListener(IPAddress.Parse(host), port);
            fakeMasterListener.Start();

            // 2. Запускаем Слейва в отдельной задаче
            var slaveConnector = new SlaveConnector(logger);
            Task slaveTask = slaveConnector.ConnectAndListenAsync(host, port, cts.Token);

            try
            {
                // 3. Мастер принимает подключение Слейва
                using var masterClientSocket = await fakeMasterListener.AcceptTcpClientAsync();
                using var stream = masterClientSocket.GetStream();
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                using var reader = new StreamReader(stream, Encoding.UTF8);

                // 4. Мастер отправляет задачу (TaskMessage)
                var taskMsg = new MasterTaskMessage
                {
                    PartId = 123,
                    TextPart = "Slave test. Slave works."
                };
                string jsonTask = JsonConvert.SerializeObject(taskMsg);
                await writer.WriteLineAsync(jsonTask);

                // 5. Читаем ответ от Слейва
                string jsonResponse = await reader.ReadLineAsync();
                Assert.False(string.IsNullOrEmpty(jsonResponse), "Slave returned empty response");

                // Десериализуем ответ (используем модель Мастера, так как JSON идентичен)
                var resultMsg = JsonConvert.DeserializeObject<MasterResultMessage>(jsonResponse);

                // Assert
                Assert.Equal(123, resultMsg.PartId);
                Assert.Equal(2, resultMsg.Frequencies["slave"]);
                Assert.Equal(1, resultMsg.Frequencies["test"]);
                Assert.Equal(1, resultMsg.Frequencies["works"]);
            }
            finally
            {
                cts.Cancel();
                fakeMasterListener.Stop();
                // Ждем завершения слейва, игнорируя отмену
                try { await slaveTask; } catch (OperationCanceledException) { } catch { }
            }
        }
    }

    public class IntegrationTests
    {
        [Fact]
        public void ClientRequest_Serialization_Works()
        {
            var req = new ClientRequest { Text = "Test Data" };
            string json = JsonConvert.SerializeObject(req);
            var deserialized = JsonConvert.DeserializeObject<ClientRequest>(json);
            Assert.Equal("Test Data", deserialized.Text);
        }

        [Fact]
        public async Task FullSystem_ClientToMaster_NoSlaves_Integration()
        {
            int port = TestUtils.GetFreeTcpPort();
            string host = "127.0.0.1";
            Action<string> logger = (s) => { };

            var slaveManager = new MasterSlaveManager(logger);
            var textProcessor = new MasterProcessor(slaveManager, logger);
            var listener = new MasterListener(logger);
            var cts = new CancellationTokenSource();

            try
            {
                listener.Start(port, (tcpClient) =>
                {
                    var handler = new MasterClientHandler(tcpClient, textProcessor, logger);
                    return handler.HandleClientAsync();
                }, cts.Token);

                var client = new ClientMasterClient(host, port);
                ClientFinalResult result = await client.SendRequestAsync("Integration test.");

                Assert.Equal(1, result.Frequencies["integration"]);
            }
            finally
            {
                listener.Stop();
                cts.Cancel();
            }
        }

        // НОВЫЙ ТЕСТ: Полная топология (Клиент -> Мастер -> Слейв)
        [Fact]
        public async Task FullSystem_Client_Master_Slave_Integration()
        {
            // Нам нужно два порта: один для Клиентов, один для Слейвов
            int clientPort = TestUtils.GetFreeTcpPort();
            int slavePort = TestUtils.GetFreeTcpPort();
            // Если порты совпали (маловероятно, но бывает), берем следующий
            if (clientPort == slavePort) slavePort++;

            string host = "127.0.0.1";
            Action<string> logger = (s) => { /* System.Diagnostics.Debug.WriteLine(s); */ };
            var cts = new CancellationTokenSource();

            // --- 1. Настройка Мастера ---
            var slaveManager = new MasterSlaveManager(logger);
            var textProcessor = new MasterProcessor(slaveManager, logger);

            // Слушатель для Клиентов
            var clientListener = new MasterListener(logger);
            // Слушатель для Слейвов
            var slaveListener = new MasterListener(logger);

            // --- 2. Запуск Слейва ---
            SlaveConnector slaveApp = new SlaveConnector(logger);
            Task slaveTask = null;

            try
            {
                // Запускаем порт для слейвов на Мастере
                slaveListener.Start(slavePort, async (socket) =>
                {
                    await slaveManager.AddSlaveAsync(socket, cts.Token);
                }, cts.Token);

                // Подключаем реального Слейва к Мастеру
                // Запускаем в фоне
                slaveTask = slaveApp.ConnectAndListenAsync(host, slavePort, cts.Token);

                // Даем немного времени на рукопожатие (подключение)
                await Task.Delay(500);

                // Проверяем, что Мастер увидел Слейва
                Assert.NotEmpty(slaveManager.ConnectedSlaves);

                // --- 3. Запуск Клиентского порта и Запрос ---
                clientListener.Start(clientPort, (socket) =>
                {
                    var handler = new MasterClientHandler(socket, textProcessor, logger);
                    return handler.HandleClientAsync();
                }, cts.Token);

                var client = new ClientMasterClient(host, clientPort);
                string text = "Distributed computing is cool. Computing distributed.";

                // Act
                ClientFinalResult result = await client.SendRequestAsync(text);

                // Assert
                Assert.NotNull(result);
                // Проверяем математику
                Assert.Equal(2, result.Frequencies["distributed"]);
                Assert.Equal(2, result.Frequencies["computing"]);
                Assert.Equal(1, result.Frequencies["is"]);
                Assert.Equal(1, result.Frequencies["cool"]);

                // Проверяем, что работа действительно была распределена (Parts > 0)
                // Если слейв отработал, у нас должны быть части результата
                Assert.NotNull(result.Parts);
                Assert.True(result.Parts.Count > 0, "Processing should be distributed into parts");
            }
            finally
            {
                // Cleanup
                cts.Cancel();
                clientListener.Stop();
                slaveListener.Stop();
                slaveApp.Disconnect();

                try { if (slaveTask != null) await slaveTask; } catch { }
            }
        }
    }

    // Вспомогательный класс
    public static class TestUtils
    {
        public static int GetFreeTcpPort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }
    }
}