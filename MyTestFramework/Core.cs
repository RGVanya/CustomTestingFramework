using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace MyTestFramework
{
    public class MyAssertException : Exception
    {
        public MyAssertException(string message) : base(message) { }
    }

    public class TestContext
    {
        private readonly Dictionary<string, object> _data = new();
        public void Set(string key, object value) => _data[key] = value;
        public T Get<T>(string key) => (T)_data[key];
    }

    // [ДОБАВЛЕНО] Конфигурация тестов — позволяет задать максимальную степень параллелизма.
    // MaxDegreeOfParallelism ограничивает количество одновременно выполняемых тестов.
    public class TestConfiguration
    {
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
    }

    // [ДОБАВЛЕНО] Перечисление статусов результата теста.
    public enum TestStatus { Passed, Failed, Timeout }

    // [ДОБАВЛЕНО] Структура для хранения результата одного теста.
    // Используется для сбора результатов из параллельных потоков.
    public class TestResult
    {
        public string ClassName { get; set; } = "";
        public string MethodName { get; set; } = "";
        public string Arguments { get; set; } = "";
        public TestStatus Status { get; set; }
        public string? Message { get; set; }
        public int ThreadId { get; set; }
        public long ElapsedMs { get; set; }
    }

    // [ДОБАВЛЕНО] Потокобезопасный логгер для вывода результатов тестирования.
    // Использует lock для синхронизации записи в консоль и файл,
    // чтобы результаты из разных потоков не перемешивались.
    public class ThreadSafeTestLogger
    {
        private readonly object _lock = new();
        private readonly string? _logFilePath;

        public ThreadSafeTestLogger(string? logFilePath = null)
        {
            _logFilePath = logFilePath;
            if (_logFilePath != null)
                File.WriteAllText(_logFilePath, $"Test Run Started: {DateTime.Now}\n\n");
        }

        public void LogResult(TestResult result)
        {
            string status = result.Status switch
            {
                TestStatus.Passed  => "PASSED",
                TestStatus.Failed  => "FAILED",
                TestStatus.Timeout => "TIMEOUT",
                _ => "UNKNOWN"
            };

            string args = string.IsNullOrEmpty(result.Arguments) ? "" : $"({result.Arguments})";
            string line = $"[Thread {result.ThreadId,2}] {result.ClassName}.{result.MethodName}{args} " +
                          $"-> {status} ({result.ElapsedMs}ms)";
            if (!string.IsNullOrEmpty(result.Message))
                line += $" : {result.Message}";

            lock (_lock)
            {
                Console.ForegroundColor = result.Status switch
                {
                    TestStatus.Passed  => ConsoleColor.Green,
                    TestStatus.Failed  => ConsoleColor.Red,
                    TestStatus.Timeout => ConsoleColor.Yellow,
                    _ => ConsoleColor.White
                };
                Console.WriteLine(line);
                Console.ResetColor();

                if (_logFilePath != null)
                    File.AppendAllText(_logFilePath, line + "\n");
            }
        }

        public void LogInfo(string message)
        {
            lock (_lock)
            {
                Console.WriteLine(message);
                if (_logFilePath != null)
                    File.AppendAllText(_logFilePath, message + "\n");
            }
        }
    }
}
