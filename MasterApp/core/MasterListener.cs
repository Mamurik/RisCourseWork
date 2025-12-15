using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MasterApp.core
{
    public class MasterListener
    {
        private TcpListener _listener;
        private Action<string> _logAction;
        private Func<TcpClient, Task> _clientConnectedHandler;
        private CancellationTokenSource _listenerCts;

        public bool IsListening => _listener != null && _listener.Server.IsBound;

        public MasterListener(Action<string> logAction)
        {
            _logAction = logAction;
        }

        public void Start(int port, Func<TcpClient, Task> clientConnectedHandler, CancellationToken externalToken)
        {
            if (IsListening)
            {
                _logAction($"Listener already started on port {port}");
                return;
            }

            _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _logAction($"Listener started on {port}");
            _clientConnectedHandler = clientConnectedHandler;
            _ = AcceptConnectionsLoop(_listenerCts.Token);
        }

        public void Stop()
        {
            if (_listener == null) return;

            _listenerCts?.Cancel();
            _listener.Stop();
            _listener = null;
            _logAction("Listener stopped");
        }

        private async Task AcceptConnectionsLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    _logAction($"Connection accepted from: {client.Client.RemoteEndPoint}");
                    _ = _clientConnectedHandler(client);
                }
                catch (OperationCanceledException)
                {
                    _logAction("Accept connections loop cancelled.");
                    break;
                }
                catch (ObjectDisposedException)
                {
                    _logAction("Listener disposed during accept operation.");
                    break;
                }
                catch (Exception ex)
                {
                    _logAction($"AcceptConnectionsLoop error: {ex.Message}");
                    await Task.Delay(100, token);
                }
            }
        }
    }
}