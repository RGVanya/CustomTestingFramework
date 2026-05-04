using System;
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

        var baseItems = CollectTestItems(testClasses);
        logger.LogInfo($"Найдено тестовых классов: {testClasses.Count}");
        logger.LogInfo($"Найдено тестовых методов (c учетом TestCase): {baseItems.Count}");

        var plan = BuildLoadPlan(baseItems, requiredRuns: 50);
        logger.LogInfo($"Сформировано запусков тестов: {plan.TotalItems}");

        var results = new ConcurrentBag<TestResult>();
        var remaining = new CountdownEvent(plan.TotalItems);

        using var pool = new DynamicThreadPool(
            minWorkers: 2,
            maxWorkers: 8,
            idleTimeout: TimeSpan.FromSeconds(3),
            scaleCheckInterval: TimeSpan.FromMilliseconds(500),
            queueWaitScaleThreshold: TimeSpan.FromMilliseconds(700),
            hungTaskThreshold: TimeSpan.FromSeconds(5),
            log: logger.LogInfo);

        using var monitorStop = new CancellationTokenSource();
        var monitor = new Thread(() => MonitorPoolLoop(pool, logger, monitorStop.Token))
        {
            IsBackground = true,
            Name = "PoolMonitor"
        };
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

            if (batch.DelayAfterMs > 0)
            {
                Thread.Sleep(batch.DelayAfterMs);
            }
        }

        remaining.Wait();
        totalSw.Stop();

        monitorStop.Cancel();
        monitor.Join(TimeSpan.FromSeconds(2));

        var finalResults = results.ToList();
        PrintSummary(finalResults, totalSw.ElapsedMilliseconds, logger);
        logger.LogInfo("Демонстрация динамического пула завершена.");
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

        // Одиночные подачи
        for (var i = 0; i < 8 && idx < generated.Count; i++)
        {
            batches.Add(new LoadBatch(new[] { generated[idx++] }, DelayAfterMs: 450));
        }

        // Пиковая нагрузка
        while (idx < generated.Count)
        {
            var take = Math.Min(10, generated.Count - idx);
            batches.Add(new LoadBatch(generated.Skip(idx).Take(take).ToArray(), DelayAfterMs: 100));
            idx += take;

            // Период бездействия между волнами
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
                var testCases = method.GetCustomAttributes<TestCaseAttribute>().ToList();

                if (testAttr is null && !testCases.Any())
                {
                    continue;
                }

                var cases = testCases.Any()
                    ? testCases.Select(c => c.Parameters).ToList()
                    : new List<object[]?> { null };

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

        return items;
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
}

internal sealed record LoadBatch(IReadOnlyList<TestItem> Items, int DelayAfterMs);
internal sealed record LoadPlan(IReadOnlyList<LoadBatch> Batches, int TotalItems);

