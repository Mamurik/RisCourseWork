using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.ObjectModel;
using System.ComponentModel;
using MasterApp.core;

namespace MasterApp
{
    public partial class MainWindow : Window
    {
        private MasterListener _clientListener;
        private MasterListener _slaveListener;
        private CancellationTokenSource _cts = new();
        private SlaveManager _slaveManager;
        private TextProcessor _textProcessor;

        public ObservableCollection<SlaveInfo> DisplayedSlaves { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            DisplayedSlaves = new ObservableCollection<SlaveInfo>();
            SlavesGrid.ItemsSource = DisplayedSlaves;

            _slaveManager = new SlaveManager(Log);
            _textProcessor = new TextProcessor(_slaveManager, Log);
        }

        private void Log(string s)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\n");
                LogBox.ScrollToEnd();
            });
        }

        private void UpdateSlavesGrid()
        {
            Dispatcher.Invoke(() =>
            {
                DisplayedSlaves.Clear();
                foreach (var slave in _slaveManager.ConnectedSlaves)
                {
                    DisplayedSlaves.Add(slave);
                }
            });
        }

        private void StartSlaveListenerBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_slaveListener != null && _slaveListener.IsListening)
            {
                Log("Slave listener already started.");
                return;
            }

            int port;
            if (!int.TryParse(SlavePortText.Text, out port))
            {
                Log("Invalid port number for slave listener.");
                return;
            }

            _slaveListener = new MasterListener(Log);
            _slaveListener.Start(port, async (socket) =>
            {
                await _slaveManager.AddSlaveAsync(socket, _cts.Token);
                UpdateSlavesGrid();
            }, _cts.Token);

            UpdateSlavesGrid();
        }

        private void StartClientListenerBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_clientListener != null && _clientListener.IsListening)
            {
                Log("Client listener already started.");
                return;
            }

            int port;
            if (!int.TryParse(ClientPortText.Text, out port))
            {
                Log("Invalid port number for client listener.");
                return;
            }

            _clientListener = new MasterListener(Log);
            _clientListener.Start(port, (clientTcp) =>
            {
                _ = new ClientHandler(clientTcp, _textProcessor, Log).HandleClientAsync();
                return Task.CompletedTask;
            }, _cts.Token);
        }

        private void ClearLogBtn_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

        protected override void OnClosed(EventArgs e)
        {
            _cts.Cancel();
            _slaveListener?.Stop();
            _clientListener?.Stop();

            foreach (var slave in _slaveManager.ConnectedSlaves)
            {
                try
                {
                    slave.Tcp.Close();
                }
                catch (Exception ex)
                {
                    Log($"Error closing slave {slave.Id} TCP connection: {ex.Message}");
                }
            }

            _cts.Dispose();
            base.OnClosed(e);
        }
    }
}   