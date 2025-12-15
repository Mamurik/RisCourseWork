using System;
using System.Collections.Generic;

namespace SharedModels
{
    public class FileData
    {
        public string FileName { get; set; }
        public string Content { get; set; }
    }

    public class ClientRequest
    {
        public List<FileData> Files { get; set; } = new List<FileData>();
        public List<string> Keywords { get; set; } = new List<string>();
    }

    // Сообщение от Мастера к Слейву: "Обработай этот файл и найди эти слова"
    public class TaskMessage
    {
        public int TaskId { get; set; }
        public string FileName { get; set; }
        public string Content { get; set; }
        public List<string> Keywords { get; set; }
    }

    // Ответ от Слейва: "В файле X нашел такие-то слова столько-то раз"
    public class ResultMessage
    {
        public int TaskId { get; set; }
        public string FileName { get; set; }

        // Ключевое слово -> Количество вхождений
        public Dictionary<string, int> KeywordCounts { get; set; }

        // Общее количество слов в файле (нужно для процента)
        public int TotalWordCount { get; set; }

        public long ProcessingMs { get; set; }
        public string Error { get; set; }
    }

    // Итоговый результат для Клиента
    public class FinalResult
    {
        // Словарь: Ключевое слово -> (Имя файла -> Процент)
        // Пример: "word1": { "file1.txt": 0.5, "file2.txt": 1.2 }
        public Dictionary<string, Dictionary<string, double>> MatrixData { get; set; }

        public List<string> FileNames { get; set; } // Чтобы знать порядок колонок
        public long TotalProcessingMs { get; set; }
    }
}