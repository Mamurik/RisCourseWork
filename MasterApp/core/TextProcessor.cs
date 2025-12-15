using SharedModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MasterApp.core
{
    public class TextProcessor
    {
        private readonly SlaveManager _slaveManager;
        private readonly Action<string> _logAction;

        public TextProcessor(SlaveManager slaveManager, Action<string> logAction)
        {
            _slaveManager = slaveManager;
            _logAction = logAction;
        }

        public async Task<FinalResult> DistributeAndCollectAsync(ClientRequest request)
        {
            var slaveList = _slaveManager.ConnectedSlaves;
            int slaveCount = slaveList.Count;

            // Если слейвов нет, возвращаем пустой результат (или можно кинуть ошибку)
            if (slaveCount == 0)
            {
                _logAction("Ошибка: Нет подключенных слейвов. Обработка невозможна.");
                return new FinalResult
                {
                    MatrixData = new Dictionary<string, Dictionary<string, double>>(),
                    FileNames = new List<string>()
                };
            }

            _logAction($"Начало обработки: {request.Files.Count} файлов, {request.Keywords.Count} ключевых слов. Слейвов: {slaveCount}.");

            var tasks = new List<Task<ResultMessage>>();

            // 1. Распределение задач (Round-Robin)
            // Файл 1 -> Слейв 1, Файл 2 -> Слейв 2, Файл 3 -> Слейв 1 ...
            for (int i = 0; i < request.Files.Count; i++)
            {
                var fileData = request.Files[i];
                var slave = slaveList[i % slaveCount]; // Выбираем слейва по кругу
                int taskId = i;

                // Запускаем асинхронную задачу
                tasks.Add(Task.Run(async () =>
                {
                    // Обратите внимание: передаем контент и список слов
                    return await _slaveManager.SendTaskToSlaveAsync(slave, taskId, fileData.FileName, fileData.Content, request.Keywords);
                }));
            }

            // 2. Ожидание всех результатов
            var results = await Task.WhenAll(tasks);

            // 3. Формирование матрицы (Словарь в Словаре)
            // Структура: [КлючевоеСлово] -> { [ИмяФайла] : Процент }
            var matrix = new Dictionary<string, Dictionary<string, double>>();

            // Предварительно заполняем словарь ключами, чтобы порядок был красивым
            foreach (var kw in request.Keywords)
            {
                matrix[kw] = new Dictionary<string, double>();
            }

            var allFileNames = new List<string>();

            // 4. Заполнение данными
            foreach (var res in results)
            {
                // Если слейв вернул ошибку, логируем и пропускаем (в таблице будет 0 или пусто)
                if (!string.IsNullOrEmpty(res.Error))
                {
                    _logAction($"Ошибка в задаче {res.TaskId} ({res.FileName}): {res.Error}");
                    allFileNames.Add(res.FileName); // Все равно добавляем имя файла, чтобы колонка создалась
                    continue;
                }

                allFileNames.Add(res.FileName);

                foreach (var kw in request.Keywords)
                {
                    int count = 0;
                    // Проверяем, нашел ли слейв это слово
                    if (res.KeywordCounts != null && res.KeywordCounts.ContainsKey(kw))
                    {
                        count = res.KeywordCounts[kw];
                    }

                    // Считаем процент: (Кол-во вхождений / Общее кол-во слов) * 100
                    double percent = 0.0;
                    if (res.TotalWordCount > 0)
                    {
                        percent = ((double)count / res.TotalWordCount) * 100.0;
                    }

                    // Записываем в матрицу
                    if (matrix.ContainsKey(kw))
                    {
                        matrix[kw][res.FileName] = percent;
                    }
                }
            }

            _logAction("Обработка завершена. Результат сформирован.");

            return new FinalResult
            {
                FileNames = allFileNames,
                MatrixData = matrix,
                TotalProcessingMs = 0 // Можно замерить Stopwatch во внешнем коде
            };
        }
    }
}