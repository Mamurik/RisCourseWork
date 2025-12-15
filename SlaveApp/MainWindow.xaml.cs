using SlaveApp.Core;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SlaveApp
{
    public partial class MainWindow : Window
    {
        private MasterConnector _masterConnector;
        private CancellationTokenSource _cts;

        public MainWindow()
        {
            InitializeComponent();
            _masterConnector = new MasterConnector(Log);
        }

        private void Log(string s)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\n");
                LogBox.ScrollToEnd();
            });
        }

        private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string host = HostText.Text;
                int port = int.Parse(PortText.Text);
                _cts = new CancellationTokenSource();
                await _masterConnector.ConnectAndListenAsync(host, port, _cts.Token);
            }
            catch (Exception ex)
            {
                Log($"Connect error: {ex.Message}");
            }
        }

        private void DisconnectBtn_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _masterConnector.Disconnect();
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            _masterConnector?.Disconnect();
            base.OnClosed(e);
        }
    }
}