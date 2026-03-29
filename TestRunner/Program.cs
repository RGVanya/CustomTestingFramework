using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MyTestFramework;

namespace TestRunner
{
    class Program
    {
        static async Task Main()
        {
            // [ДОБАВЛЕНО] Конфигурация: задаём максимальное количество одновременно выполняемых тестов.
            var config = new TestConfiguration
            {
                MaxDegreeOfParallelism = 4
            };

            // [ДОБАВЛЕНО] Потокобезопасный логгер — синхронизирует вывод в консоль и пишет в файл.
            var logger = new ThreadSafeTestLogger("test_results.log");

            var testAssembly = typeof(AppTests.AuthServiceTests).Assembly;

            var testClasses = testAssembly.GetTypes()
                .Where(t => t.GetCustomAttribute<TestClassAttribute>() != null)
                .ToList();

            // Собираем все тестовые задания (класс + метод + аргументы) в единый список
            var allTestItems = CollectTestItems(testClasses);

            logger.LogInfo($"Обнаружено тестовых классов: {testClasses.Count}");
            logger.LogInfo($"Обнаружено тестовых методов (с учётом TestCase): {allTestItems.Count}");
            logger.LogInfo($"MaxDegreeOfParallelism: {config.MaxDegreeOfParallelism}");

            // ====================================================================
            // [ДОБАВЛЕНО] СРАВНЕНИЕ ЭФФЕКТИВНОСТИ: сначала последовательный запуск,
            // затем параллельный. Время обоих запусков выводится для наглядности.
            // ====================================================================

            // --- Последовательный запуск ---
            logger.LogInfo("\n========== ПОСЛЕДОВАТЕЛЬНЫЙ ЗАПУСК ==========");
            var sequentialResults = await RunTestsSequentially(allTestItems, logger);
            var sequentialTime = sequentialResults.totalMs;

            // --- Параллельный запуск ---
            logger.LogInfo("\n========== ПАРАЛЛЕЛЬНЫЙ ЗАПУСК ==========");
            var parallelResults = await RunTestsInParallel(allTestItems, config, logger);
            var parallelTime = parallelResults.totalMs;

            // --- Итоги ---
            logger.LogInfo("\n========== ИТОГИ ==========");
            PrintSummary(sequentialResults.results, "Последовательный", sequentialTime, logger);
            PrintSummary(parallelResults.results, "Параллельный", parallelTime, logger);

            double speedup = sequentialTime > 0 ? (double)sequentialTime / parallelTime : 1;
            logger.LogInfo($"\nУскорение: x{speedup:F2} (параллельный быстрее в {speedup:F2} раз)");

            logger.LogInfo("\nТестирование завершено. Нажмите любую клавишу...");
            Console.ReadKey();
        }

        // [ДОБАВЛЕНО] Сбор всех тестовых элементов из всех классов в единый плоский список.
        // Каждый элемент — это (тип класса, метод, аргументы).
        static List<TestItem> CollectTestItems(List<Type> testClasses)
        {
            var items = new List<TestItem>();

            foreach (var type in testClasses)
            {
                var methods = type.GetMethods();
                foreach (var method in methods)
                {
                    var testAttr = method.GetCustomAttribute<TestMethodAttribute>();
                    var testCases = method.GetCustomAttributes<TestCaseAttribute>().ToList();

                    if (testAttr != null || testCases.Any())
                    {
                        var cases = testCases.Any()
                            ? testCases.Select(c => c.Parameters).ToList()
                            : new List<object[]> { null! };

                        foreach (var args in cases)
                        {
                            items.Add(new TestItem
                            {
                                ClassType = type,
                                Method = method,
                                TestAttribute = testAttr,
                                Arguments = args,
                                TimeoutMs = method.GetCustomAttribute<TimeoutAttribute>()?.Milliseconds
                            });
                        }
                    }
                }
            }
            return items;
        }

        // [ДОБАВЛЕНО] Последовательный запуск всех тестов — для сравнения с параллельным.
        static async Task<(List<TestResult> results, long totalMs)> RunTestsSequentially(
            List<TestItem> items, ThreadSafeTestLogger logger)
        {
            var results = new List<TestResult>();
            var sw = Stopwatch.StartNew();

            foreach (var item in items)
            {
                var result = await ExecuteSingleTest(item);
                results.Add(result);
                logger.LogResult(result);
            }

            sw.Stop();
            logger.LogInfo($"Последовательное время: {sw.ElapsedMilliseconds}ms");
            return (results, sw.ElapsedMilliseconds);
        }

        // [ДОБАВЛЕНО] Параллельный запуск всех тестов с ограничением степени параллелизма.
        // Использует SemaphoreSlim для контроля количества одновременных потоков
        // и Task.WhenAll для ожидания завершения всех тестов.
        // Параллелизм реализован на уровне методов: каждый тестовый метод запускается
        // как отдельная задача (Task), независимо от того, к какому классу он принадлежит.
        static async Task<(List<TestResult> results, long totalMs)> RunTestsInParallel(
            List<TestItem> items, TestConfiguration config, ThreadSafeTestLogger logger)
        {
            var results = new ConcurrentBag<TestResult>();
            var semaphore = new SemaphoreSlim(config.MaxDegreeOfParallelism);
            var sw = Stopwatch.StartNew();

            var tasks = items.Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var result = await ExecuteSingleTest(item);
                    results.Add(result);
                    logger.LogResult(result);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            sw.Stop();
            logger.LogInfo($"Параллельное время: {sw.ElapsedMilliseconds}ms");
            return (results.ToList(), sw.ElapsedMilliseconds);
        }

        // [ДОБАВЛЕНО] Запуск одного теста с поддержкой таймаута.
        // Если на методе есть атрибут [Timeout(ms)], тест выполняется с ограничением.
        // При превышении таймаута — отмена через CancellationToken и статус TIMEOUT.
        static async Task<TestResult> ExecuteSingleTest(TestItem item)
        {
            var result = new TestResult
            {
                ClassName = item.ClassType.Name,
                MethodName = item.Method.Name,
                Arguments = item.Arguments != null ? string.Join(",", item.Arguments) : "",
                ThreadId = Environment.CurrentManagedThreadId
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var testContext = new TestContext();

                object instance;
                var ctors = item.ClassType.GetConstructors();
                if (ctors.Any(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == typeof(TestContext)))
                    instance = Activator.CreateInstance(item.ClassType, testContext)!;
                else
                    instance = Activator.CreateInstance(item.ClassType)!;

                // BeforeEach
                var beforeEach = item.ClassType.GetMethods()
                    .FirstOrDefault(m => m.GetCustomAttribute<BeforeEachAttribute>() != null);
                beforeEach?.Invoke(instance, null);

                // [ДОБАВЛЕНО] Выполнение теста с таймаутом через Task.Run + CancellationTokenSource.
                // Если таймаут задан — тест оборачивается в Task.WhenAny с Task.Delay.
                if (item.TimeoutMs.HasValue)
                {
                    using var cts = new CancellationTokenSource();
                    var testTask = Task.Run(async () =>
                    {
                        object res = item.Method.Invoke(instance, item.Arguments);
                        if (res is Task t) await t;
                    }, cts.Token);

                    var timeoutTask = Task.Delay(item.TimeoutMs.Value, cts.Token);

                    var completed = await Task.WhenAny(testTask, timeoutTask);
                    if (completed == timeoutTask)
                    {
                        cts.Cancel();
                        stopwatch.Stop();
                        result.Status = TestStatus.Timeout;
                        result.Message = $"Тест превысил таймаут {item.TimeoutMs}ms";
                        result.ElapsedMs = stopwatch.ElapsedMilliseconds;

                        var afterEach = item.ClassType.GetMethods()
                            .FirstOrDefault(m => m.GetCustomAttribute<AfterEachAttribute>() != null);
                        afterEach?.Invoke(instance, null);
                        return result;
                    }

                    await testTask;
                }
                else
                {
                    object res = item.Method.Invoke(instance, item.Arguments)!;
                    if (res is Task t) await t;
                }

                stopwatch.Stop();
                result.Status = TestStatus.Passed;
                result.Message = item.TestAttribute?.Description ?? "";
                result.ElapsedMs = stopwatch.ElapsedMilliseconds;

                // AfterEach
                var afterEachMethod = item.ClassType.GetMethods()
                    .FirstOrDefault(m => m.GetCustomAttribute<AfterEachAttribute>() != null);
                afterEachMethod?.Invoke(instance, null);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.Status = TestStatus.Failed;
                result.Message = ex.InnerException?.Message ?? ex.Message;
                result.ElapsedMs = stopwatch.ElapsedMilliseconds;
            }

            return result;
        }

        static void PrintSummary(List<TestResult> results, string mode, long totalMs,
            ThreadSafeTestLogger logger)
        {
            int passed = results.Count(r => r.Status == TestStatus.Passed);
            int failed = results.Count(r => r.Status == TestStatus.Failed);
            int timeout = results.Count(r => r.Status == TestStatus.Timeout);

            logger.LogInfo($"[{mode}] Всего: {results.Count} | " +
                           $"Passed: {passed} | Failed: {failed} | Timeout: {timeout} | " +
                           $"Время: {totalMs}ms");
        }
    }

    // [ДОБАВЛЕНО] Вспомогательная структура для описания одного тестового элемента.
    // Хранит всю необходимую информацию для запуска конкретного теста.
    class TestItem
    {
        public Type ClassType { get; set; } = null!;
        public MethodInfo Method { get; set; } = null!;
        public TestMethodAttribute? TestAttribute { get; set; }
        public object[]? Arguments { get; set; }
        public int? TimeoutMs { get; set; }
    }
}
