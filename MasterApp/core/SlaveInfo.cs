using System.IO;
using System.Net.Sockets;

namespace MasterApp.core
{
    public class SlaveInfo
    {
        public int Id { get; set; }
        public TcpClient Tcp { get; set; }
        public string Endpoint { get; set; }
        public string Status { get; set; }

        // Добавляем постоянные читатель и писатель
        public StreamReader Reader { get; set; }
        public StreamWriter Writer { get; set; }
    }
}