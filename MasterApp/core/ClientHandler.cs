using Newtonsoft.Json;
using SharedModels;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MasterApp.core
{
    public class ClientHandler
    {
        private readonly TcpClient _client;
        private readonly TextProcessor _textProcessor;
        private readonly Action<string> _logAction;

        public ClientHandler(TcpClient client, TextProcessor textProcessor, Action<string> logAction)
        {
            _client = client;
            _textProcessor = textProcessor;
            _logAction = logAction;
        }

        public async Task HandleClientAsync()
        {
            try
            {
                using var stream = _client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                string line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                {
                    _logAction($"Received empty from client {_client.Client.RemoteEndPoint}");
                    return;
                }

                var req = JsonConvert.DeserializeObject<ClientRequest>(line);
                _logAction($"Received request from client {_client.Client.RemoteEndPoint}. Splitting and distributing to slaves...");

                var swTotal = System.Diagnostics.Stopwatch.StartNew();
                var final = await _textProcessor.DistributeAndCollectAsync(req);
                swTotal.Stop();
                final.TotalProcessingMs = swTotal.ElapsedMilliseconds;

                string resultJson = JsonConvert.SerializeObject(final);
                await writer.WriteLineAsync(resultJson);
                _logAction($"Sent final result to client {_client.Client.RemoteEndPoint}");
            }
            catch (Exception ex)
            {
                _logAction($"HandleClient error for client {_client.Client.RemoteEndPoint}: {ex.Message}");
            }
            finally
            {
                _client.Close();
            }
        }
    }
}