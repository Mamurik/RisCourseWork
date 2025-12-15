using Newtonsoft.Json;
using SharedModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SlaveApp.Core
{
    public class MasterConnector
    {
        private TcpClient _client;
        private Action<string> _logAction;

        public MasterConnector(Action<string> logAction)
        {
            _logAction = logAction;
        }

        public async Task ConnectAndListenAsync(string host, int port, CancellationToken token)
        {
            _client = new TcpClient();
            try
            {
                await _client.ConnectAsync(host, port);
                _logAction($"Подключено к Master {host}:{port}");

                using var stream = _client.GetStream();

                // ВАЖНО: new UTF8Encoding(false) отключает BOM (Byte Order Mark).
                // Это предотвращает появление символа '﻿' в начале JSON строки, из-за которого падал парсер.
                using var reader = new StreamReader(stream, new UTF8Encoding(false));
                using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

                // Цикл прослушивания задач
                while (!token.IsCancellationRequested && _client.Connected)
                {
                    // Читаем строку (ожидаем JSON с задачей)
                    string line = await reader.ReadLineAsync();

                    if (line == null)
                    {
                        // Мастер разорвал соединение
                        _logAction("Мастер отключился.");
                        break;
                    }
                    if (string.IsNullOrEmpty(line)) continue;

                    try
                    {
                        // 1. Десериализация
                        var taskMsg = JsonConvert.DeserializeObject<TaskMessage>(line);
                        _logAction($"Получена задача #{taskMsg.TaskId}: файл '{taskMsg.FileName}', слов для поиска: {taskMsg.Keywords?.Count ?? 0}");

                        var sw = Stopwatch.StartNew();

                        // 2. Обработка текста
                        var allWords = SplitWords(taskMsg.Content ?? "");
                        int totalWords = allWords.Count;

                        var counts = new Dictionary<string, int>();

                        // Инициализируем нулями
                        if (taskMsg.Keywords != null)
                        {
                            foreach (var k in taskMsg.Keywords) counts[k] = 0;

                            // Считаем вхождения
                            foreach (var word in allWords)
                            {
                                // Ищем слово в списке ключевых (Case Insensitive)
                                // Для больших текстов лучше использовать HashSet, но для лабы FirstOrDefault сойдет
                                var match = taskMsg.Keywords.FirstOrDefault(k => k.Equals(word, StringComparison.OrdinalIgnoreCase));
                                if (match != null)
                                {
                                    counts[match]++;
                                }
                            }
                        }

                        sw.Stop();

                        // 3. Формирование ответа
                        var result = new ResultMessage
                        {
                            TaskId = taskMsg.TaskId,
                            FileName = taskMsg.FileName,
                            KeywordCounts = counts,
                            TotalWordCount = totalWords,
                            ProcessingMs = sw.ElapsedMilliseconds
                        };

                        // 4. Отправка ответа
                        string jsonResp = JsonConvert.SerializeObject(result);
                        await writer.WriteLineAsync(jsonResp);

                        _logAction($"Результат для '{taskMsg.FileName}' отправлен. Обработано за {sw.ElapsedMilliseconds} мс. Слов: {totalWords}");
                    }
                    catch (Exception ex)
                    {
                        _logAction($"Ошибка обработки задачи: {ex.Message}");
                        // Можно отправить сообщение об ошибке мастеру, если предусмотреть протокол
                    }
                }
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка соединения: {ex.Message}");
            }
            finally
            {
                _client?.Close();
            }
        }

        public void Disconnect()
        {
            _client?.Close();
            _client = null;
            _logAction("Отключено пользователем.");
        }

        private List<string> SplitWords(string text)
        {
            // Стандартные разделители
            var separators = new char[] {
                ' ', '\r', '\n', '\t',
                ',', '.', '!', '?', ';', ':',
                '-', '—', '(', ')', '"', '\'',
                '[', ']', '{', '}', '/', '\\'
            };

            return (text ?? "")
                .Split(separators, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim().ToLowerInvariant()) // Приводим к нижнему регистру
                .ToList();
        }
    }
}