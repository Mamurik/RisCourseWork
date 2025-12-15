using Newtonsoft.Json;
using SharedModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ClientApp.Core
{
    public class MasterClient
    {
        private readonly string _host;
        private readonly int _port;

        public MasterClient(string host, int port)
        {
            _host = host;
            _port = port;
        }

        // Внутри класса MasterClient
        public async Task<FinalResult> SendRequestAsync(ClientRequest req)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_host, _port);
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string jsonReq = JsonConvert.SerializeObject(req);
            await writer.WriteLineAsync(jsonReq);

            string resp = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(resp))
            {
                throw new InvalidOperationException("Empty response from master.");
            }
            return JsonConvert.DeserializeObject<FinalResult>(resp);
        }
    }
}