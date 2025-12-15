using SharedModels;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MasterApp.core
{
    public class SlaveManager
    {
        private ConcurrentDictionary<int, SlaveInfo> _slaves = new();
        private int _nextSlaveId = 1;
        private Action<string> _logAction;

        // Семафор, чтобы не писать в один сокет одновременно из разных потоков
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public IReadOnlyList<SlaveInfo> ConnectedSlaves => _slaves.Values.ToList();

        public SlaveManager(Action<string> logAction)
        {
            _logAction = logAction;
        }

        public async Task AddSlaveAsync(TcpClient socket, CancellationToken token)
        {
            int id = Interlocked.Increment(ref _nextSlaveId);
            var stream = socket.GetStream();

            var info = new SlaveInfo
            {
                Id = id,
                Tcp = socket,
                Endpoint = socket.Client.RemoteEndPoint.ToString(),
                Status = "Connected",
                // Инициализируем один раз! Важно: без BOM (new UTF8Encoding(false))
                Writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true },
                Reader = new StreamReader(stream, new UTF8Encoding(false))
            };

            _slaves[id] = info;
            _logAction($"Slave connected: {id} - {info.Endpoint}");

            try
            {
                // Просто держим соединение живым, пока токен не отменен или сокет не закрыт
                while (!token.IsCancellationRequested && socket.Connected)
                {
                    await Task.Delay(1000, token);
                    // Можно добавить проверку ping/pong, но пока достаточно ожидания
                }
            }
            catch { /* Игнорируем ошибки ожидания */ }
            finally
            {
                _logAction($"Slave {info.Id} disconnected");
                _slaves.TryRemove(info.Id, out _);
                info.Tcp?.Close();
            }
        }

        public async Task<ResultMessage> SendTaskToSlaveAsync(SlaveInfo slave, int taskId, string fileName, string content, List<string> keywords)
        {
            // Используем семафор, чтобы гарантировать thread-safety при записи в один сокет
            // (хотя логика распределения и так дает 1 задачу на слейва, это страховка)
            await _lock.WaitAsync();
            try
            {
                if (!slave.Tcp.Connected)
                    throw new Exception("Slave disconnected");

                var taskMsg = new TaskMessage
                {
                    TaskId = taskId,
                    FileName = fileName,
                    Content = content,
                    Keywords = keywords
                };

                string json = JsonConvert.SerializeObject(taskMsg);

                // Используем сохраненные Writer/Reader
                await slave.Writer.WriteLineAsync(json);

                // Ждем ответ (таймаут 10 секунд)
                var readTask = slave.Reader.ReadLineAsync();
                var completed = await Task.WhenAny(readTask, Task.Delay(10000));

                if (completed != readTask)
                {
                    return CreateErrorResult(taskId, fileName, "Timeout");
                }

                string resp = await readTask;
                if (string.IsNullOrEmpty(resp))
                {
                    return CreateErrorResult(taskId, fileName, "Empty Response");
                }

                var res = JsonConvert.DeserializeObject<ResultMessage>(resp);
                return res;
            }
            catch (Exception ex)
            {
                _logAction($"Error sending to slave {slave.Id}: {ex.Message}");
                return CreateErrorResult(taskId, fileName, ex.Message);
            }
            finally
            {
                _lock.Release();
            }
        }

        // Вспомогательный метод создания ошибки (нужен, так как вызывается из 2 мест)
        public ResultMessage CreateErrorResult(int id, string fileName, string error)
        {
            return new ResultMessage
            {
                TaskId = id,
                FileName = fileName,
                Error = error,
                KeywordCounts = new Dictionary<string, int>(),
                TotalWordCount = 0
            };
        }
    }
}