using ClientApp.Core;
using Microsoft.Win32;
using SharedModels;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;

namespace ClientApp
{
    public partial class MainWindow : Window
    {
        private List<FileData> _selectedFiles = new List<FileData>();
        private List<string> _keywords = new List<string>();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void AddFilesBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                Title = "Выберите текстовые файлы"
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    try
                    {
                        var content = File.ReadAllText(file);
                        string name = System.IO.Path.GetFileName(file);
                        _selectedFiles.Add(new FileData { FileName = name, Content = content });
                        FilesList.Items.Add(name);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка чтения {file}: {ex.Message}");
                    }
                }
            }
        }

        private void LoadKeywordsBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt",
                Title = "Выберите файл с ключевыми словами"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string content = File.ReadAllText(dialog.FileName);
                    // Разбиваем по пробелам, запятым, переносам строк
                    _keywords = content.Split(new[] { ' ', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(k => k.Trim())
                                       .Distinct(StringComparer.OrdinalIgnoreCase)
                                       .ToList();

                    KeywordsPreview.Text = string.Join(", ", _keywords);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка чтения {dialog.FileName}: {ex.Message}");
                }
            }
        }

        private async void SendBtn_Click(object sender, RoutedEventArgs e)
        {
            string host = HostText.Text.Trim();
            if (!int.TryParse(PortText.Text.Trim(), out int port))
            {
                InfoText.Text = "Неверный порт.";
                return;
            }

            if (_selectedFiles.Count == 0 || _keywords.Count == 0)
            {
                MessageBox.Show("Загрузите хотя бы один текстовый файл и файл с ключевыми словами.");
                return;
            }

            InfoText.Text = "Отправка и обработка...";
            SendBtn.IsEnabled = false;

            try
            {
                var masterClient = new MasterClient(host, port);

                // Формируем новый тип запроса
                var request = new ClientRequest
                {
                    Files = _selectedFiles,
                    Keywords = _keywords
                };

                // MasterClient.SendRequestAsync нужно обновить (см. ниже), чтобы он принимал ClientRequest
                var result = await masterClient.SendRequestAsync(request);

                BuildMatrix(result);
                InfoText.Text = $"Готово. Время обработки: {result.TotalProcessingMs} мс";
            }
            catch (Exception ex)
            {
                InfoText.Text = $"Ошибка: {ex.Message}";
            }
            finally
            {
                SendBtn.IsEnabled = true;
            }
        }

        private void BuildMatrix(FinalResult result)
        {
            if (result == null || result.MatrixData == null) return;

            var table = new DataTable();

            // 1. Первая колонка - Ключевое слово
            table.Columns.Add("Ключевое слово", typeof(string));

            // 2. Колонки для файлов
            // ВАЖНО: Заменяем точки на подчеркивания, чтобы WPF не ломал привязку
            foreach (var fileName in result.FileNames)
            {
                // "тест1.txt" -> "тест1_txt"
                string safeColName = fileName.Replace(".", "_");

                if (!table.Columns.Contains(safeColName))
                    table.Columns.Add(safeColName, typeof(double));
            }

            // 3. Заполняем строки
            foreach (var keyword in result.MatrixData.Keys)
            {
                var row = table.NewRow();
                row["Ключевое слово"] = keyword;

                var fileData = result.MatrixData[keyword]; // Данные по этому слову

                foreach (var fileName in result.FileNames)
                {
                    // Используем то же самое безопасное имя для поиска колонки
                    string safeColName = fileName.Replace(".", "_");

                    if (fileData.ContainsKey(fileName))
                    {
                        // Округляем до 2 знаков
                        row[safeColName] = Math.Round(fileData[fileName], 2);
                    }
                    else
                    {
                        row[safeColName] = 0.0;
                    }
                }
                table.Rows.Add(row);
            }

            // Сбрасываем источник, чтобы обновить таблицу
            ResultGrid.ItemsSource = null;
            ResultGrid.ItemsSource = table.DefaultView;
        }
    }
}