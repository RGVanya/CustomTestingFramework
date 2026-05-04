using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MyTestFramework;
using MyThreadPool;

namespace TestRunner;

internal class Program
{
    static void Main()
    {
        var logger = new ThreadSafeTestLogger("test_results.log");

        var testAssembly = typeof(AppTests.AuthServiceTests).Assembly;
        var testClasses = testAssembly.GetTypes()
            .Where(t => t.GetCustomAttribute<TestClassAttribute>() is not null)
            .ToList();

        var allItems = CollectTestItems(testClasses);

        logger.LogInfo($"Найдено тестовых классов: {testClasses.Count}");
        logger.LogInfo($"Найдено тестовых методов/кейсов всего: {allItems.Count}");
        RunScenario(
            "SCENARIO 1: Smoke+Load+Priority<=2+Author=Student",
            allItems,
            item => item.Categories.Contains("Smoke")
                    || item.Categories.Contains("Load")
                    || (item.Priority.HasValue && item.Priority.Value <= 2)
                    || string.Equals(item.Author, "Student", StringComparison.OrdinalIgnoreCase),
            requiredRuns: 50,
            logger: logger);

        RunScenario(
            "SCENARIO 2: Only Smoke, Priority<=1",
            allItems,
            item => item.Categories.Contains("Smoke") && item.Priority.GetValueOrDefault(99) <= 1,
            requiredRuns: 30,
            logger: logger);

        RunScenario(
            "SCENARIO 3: Load+Timeout+Regression",
            allItems,
            item => item.Categories.Contains("Load")
                    || item.Categories.Contains("Timeout")
                    || item.Categories.Contains("Regression"),
            requiredRuns: 40,
            logger: logger);
    }

    static void RunScenario(string title, List<TestItem> allItems, Func<TestItem, bool> filter, int requiredRuns, ThreadSafeTestLogger logger)
    {
        logger.LogInfo($"\n===== {title} =====");
        var baseItems = allItems.Where(filter).ToList();
        logger.LogInfo($"После фильтрации (делегат): {baseItems.Count}");
        if (baseItems.Count == 0)
        {
            logger.LogInfo("Нет тестов для запуска в этом сценарии.");
            return;
        }

        var plan = BuildLoadPlan(baseItems, requiredRuns);
        logger.LogInfo($"Сформировано запусков тестов: {plan.TotalItems}");

        var results = new ConcurrentBag<TestResult>();
        using var remaining = new CountdownEvent(plan.TotalItems);
        using var pool = new DynamicThreadPool(
            minWorkers: 2,
            maxWorkers: 8,
            idleTimeout: TimeSpan.FromSeconds(3),
            scaleCheckInterval: TimeSpan.FromMilliseconds(500),
            queueWaitScaleThreshold: TimeSpan.FromMilliseconds(700),
            hungTaskThreshold: TimeSpan.FromSeconds(5),
            log: logger.LogInfo);

        pool.WorkerCreated += (_, e) => logger.LogInfo($"[EVENT] WorkerCreated id={e.WorkerId} reason={e.Reason}");
        pool.WorkerStopped += (_, e) => logger.LogInfo($"[EVENT] WorkerStopped id={e.WorkerId} reason={e.Reason}");
        pool.ScaledUp += (_, e) => logger.LogInfo($"[EVENT] ScaledUp id={e.WorkerId} reason={e.Reason}");
        pool.TaskFailed += (_, e) => logger.LogInfo($"[EVENT] TaskFailed worker={e.WorkerId} error={e.Exception.Message}");

        using var monitorStop = new CancellationTokenSource();
        var monitor = new Thread(() => MonitorPoolLoop(pool, logger, monitorStop.Token)) { IsBackground = true, Name = "PoolMonitor" };
        monitor.Start();

        var totalSw = Stopwatch.StartNew();
        foreach (var batch in plan.Batches)
        {
            foreach (var item in batch.Items)
            {
                pool.Enqueue(() =>
                {
                    try
                    {
                        var result = ExecuteSingleTest(item);
                        results.Add(result);
                        logger.LogResult(result);
                    }
                    catch (Exception ex)
                    {
                        logger.LogInfo($"[RUNNER][ERROR] {item.ClassType.Name}.{item.Method.Name}: {ex.Message}");
                    }
                    finally
                    {
                        remaining.Signal();
                    }
                });
            }

            if (batch.DelayAfterMs > 0) Thread.Sleep(batch.DelayAfterMs);
        }

        remaining.Wait();
        totalSw.Stop();
        monitorStop.Cancel();
        monitor.Join(TimeSpan.FromSeconds(2));
        PrintSummary(results.ToList(), totalSw.ElapsedMilliseconds, logger);
    }

    static void MonitorPoolLoop(DynamicThreadPool pool, ThreadSafeTestLogger logger, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var snapshot = pool.GetSnapshot();
            logger.LogInfo($"[MONITOR] queue={snapshot.QueueLength}, active={snapshot.ActiveWorkers}, busy={snapshot.BusyWorkers}, idle={snapshot.IdleWorkers}");
            Thread.Sleep(1000);
        }
    }

    static LoadPlan BuildLoadPlan(List<TestItem> baseItems, int requiredRuns)
    {
        var batches = new List<LoadBatch>();
        var generated = new List<TestItem>(requiredRuns);

        int cycleIndex = 0;
        while (generated.Count < requiredRuns)
        {
            generated.Add(baseItems[cycleIndex % baseItems.Count]);
            cycleIndex++;
        }

        var idx = 0;

        for (var i = 0; i < 8 && idx < generated.Count; i++)
        {
            batches.Add(new LoadBatch(new[] { generated[idx++] }, DelayAfterMs: 450));
        }

        while (idx < generated.Count)
        {
            var take = Math.Min(10, generated.Count - idx);
            batches.Add(new LoadBatch(generated.Skip(idx).Take(take).ToArray(), DelayAfterMs: 100));
            idx += take;

            if (idx < generated.Count)
            {
                batches.Add(new LoadBatch(Array.Empty<TestItem>(), DelayAfterMs: 2200));
            }
        }

        return new LoadPlan(batches, generated.Count);
    }

    static List<TestItem> CollectTestItems(List<Type> testClasses)
    {
        var items = new List<TestItem>();

        foreach (var type in testClasses)
        {
            foreach (var method in type.GetMethods())
            {
                var testAttr = method.GetCustomAttribute<TestMethodAttribute>();
                if (testAttr is null && !method.GetCustomAttributes<TestCaseAttribute>().Any() && !method.GetCustomAttributes<TestCaseSourceAttribute>().Any())
                {
                    continue;
                }

                var cases = ResolveCases(type, method);
                var timeout = method.GetCustomAttribute<TimeoutAttribute>()?.Milliseconds;
                var categories = method.GetCustomAttributes<CategoryAttribute>().Select(c => c.Name).ToArray();
                var priority = method.GetCustomAttribute<PriorityAttribute>()?.Level;
                var author = method.GetCustomAttribute<AuthorAttribute>()?.Name;

                foreach (var args in cases)
                {
                    items.Add(new TestItem
                    {
                        ClassType = type,
                        Method = method,
                        TestAttribute = testAttr,
                        Arguments = args,
                        TimeoutMs = timeout,
                        Categories = categories,
                        Priority = priority,
                        Author = author
                    });
                }
            }
        }

        return items;
    }

    static List<object[]?> ResolveCases(Type classType, MethodInfo method)
    {
        var directCases = method.GetCustomAttributes<TestCaseAttribute>()
            .Select(c => c.Parameters)
            .Cast<object[]?>()
            .ToList();

        foreach (var source in method.GetCustomAttributes<TestCaseSourceAttribute>())
        {
            var provider = classType.GetMethod(source.SourceName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException($"TestCaseSource method '{source.SourceName}' not found for {classType.Name}.{method.Name}");

            var value = provider.Invoke(null, null)
                ?? throw new InvalidOperationException($"TestCaseSource '{source.SourceName}' returned null");

            if (value is IEnumerable enumerable)
            {
                foreach (var entry in enumerable)
                {
                    if (entry is object[] arr)
                    {
                        directCases.Add(arr);
                    }
                    else
                    {
                        throw new InvalidOperationException($"TestCaseSource '{source.SourceName}' must yield object[]");
                    }
                }
            }
            else
            {
                throw new InvalidOperationException($"TestCaseSource '{source.SourceName}' must implement IEnumerable");
            }
        }

        if (directCases.Count == 0)
        {
            directCases.Add(null);
        }

        return directCases;
    }

    static TestResult ExecuteSingleTest(TestItem item)
    {
        var result = new TestResult
        {
            ClassName = item.ClassType.Name,
            MethodName = item.Method.Name,
            Arguments = item.Arguments is not null ? string.Join(",", item.Arguments) : "",
            ThreadId = Environment.CurrentManagedThreadId
        };

        var sw = Stopwatch.StartNew();

        try
        {
            var testContext = new TestContext();
            object instance;

            var ctorWithContext = item.ClassType.GetConstructors()
                .FirstOrDefault(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == typeof(TestContext));

            instance = ctorWithContext is not null
                ? Activator.CreateInstance(item.ClassType, testContext)!
                : Activator.CreateInstance(item.ClassType)!;

            var beforeEach = item.ClassType.GetMethods().FirstOrDefault(m => m.GetCustomAttribute<BeforeEachAttribute>() is not null);
            var afterEach = item.ClassType.GetMethods().FirstOrDefault(m => m.GetCustomAttribute<AfterEachAttribute>() is not null);

            beforeEach?.Invoke(instance, null);

            Exception? executionException = null;
            var worker = new Thread(() =>
            {
                try
                {
                    var raw = item.Method.Invoke(instance, item.Arguments);
                    if (raw is Task task)
                    {
                        task.GetAwaiter().GetResult();
                    }
                }
                catch (TargetInvocationException tie)
                {
                    executionException = tie.InnerException ?? tie;
                }
                catch (Exception ex)
                {
                    executionException = ex;
                }
            })
            {
                IsBackground = true,
                Name = $"TestExec-{item.ClassType.Name}.{item.Method.Name}"
            };

            worker.Start();

            var timeoutMs = item.TimeoutMs ?? Timeout.Infinite;
            var finished = worker.Join(timeoutMs);
            if (!finished)
            {
                sw.Stop();
                result.Status = TestStatus.Timeout;
                result.Message = $"Тест превысил таймаут {item.TimeoutMs}ms";
                result.ElapsedMs = sw.ElapsedMilliseconds;
                afterEach?.Invoke(instance, null);
                return result;
            }

            if (executionException is not null)
            {
                throw executionException;
            }

            sw.Stop();
            result.Status = TestStatus.Passed;
            result.Message = item.TestAttribute?.Description ?? "";
            result.ElapsedMs = sw.ElapsedMilliseconds;

            afterEach?.Invoke(instance, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.Status = TestStatus.Failed;
            result.Message = ex.Message;
            result.ElapsedMs = sw.ElapsedMilliseconds;
        }

        return result;
    }

    static void PrintSummary(List<TestResult> results, long totalMs, ThreadSafeTestLogger logger)
    {
        var passed = results.Count(r => r.Status == TestStatus.Passed);
        var failed = results.Count(r => r.Status == TestStatus.Failed);
        var timeout = results.Count(r => r.Status == TestStatus.Timeout);

        logger.LogInfo("\nИТОГИ");
        logger.LogInfo($"Всего: {results.Count} | Passed: {passed} | Failed: {failed} | Timeout: {timeout} | Время: {totalMs}ms");
    }
}

internal sealed class TestItem
{
    public required Type ClassType { get; init; }
    public required MethodInfo Method { get; init; }
    public TestMethodAttribute? TestAttribute { get; init; }
    public object[]? Arguments { get; init; }
    public int? TimeoutMs { get; init; }
    public string[] Categories { get; init; } = Array.Empty<string>();
    public int? Priority { get; init; }
    public string? Author { get; init; }
}

internal sealed record LoadBatch(IReadOnlyList<TestItem> Items, int DelayAfterMs);
internal sealed record LoadPlan(IReadOnlyList<LoadBatch> Batches, int TotalItems);
