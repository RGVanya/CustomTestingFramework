using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MyTestFramework;

namespace TestRunner
{
    class Program
    {
        static async Task Main()
        {
            // 1. Загружаем сборку с тестами
            // В данном примере мы берем сборку, где лежит класс AuthServiceTests
            var testAssembly = typeof(AppTests.AuthServiceTests).Assembly;
            var testContext = new TestContext(); // Создаем один контекст на все тесты

            var testClasses = testAssembly.GetTypes()
                .Where(t => t.GetCustomAttribute<TestClassAttribute>() != null);

            foreach (var type in testClasses)
            {
                Console.WriteLine($"\n--- Running Class: {type.Name} ---");

                // Создаем экземпляр класса теста (передаем контекст в конструктор)
                var instance = Activator.CreateInstance(type, testContext);

                var methods = type.GetMethods();
                var beforeEach = methods.FirstOrDefault(m => m.GetCustomAttribute<BeforeEachAttribute>() != null);
                var afterEach = methods.FirstOrDefault(m => m.GetCustomAttribute<AfterEachAttribute>() != null);

                foreach (var method in methods)
                {
                    var testAttr = method.GetCustomAttribute<TestMethodAttribute>();
                    var testCases = method.GetCustomAttributes<TestCaseAttribute>();

                    // Если есть тест-кейсы, запускаем каждый. Если просто метод - один раз.
                    if (testAttr != null || testCases.Any())
                    {
                        var cases = testCases.Any() ? testCases.Select(c => c.Parameters) : new[] { (object[])null };

                        foreach (var args in cases)
                        {
                            try
                            {
                                beforeEach?.Invoke(instance, null);

                                Console.Write($"Running {method.Name}({string.Join(",", args ?? new object[0])})... ");

                                // Обработка асинхронности
                                object result = method.Invoke(instance, args);
                                if (result is Task task) await task;

                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("PASSED " + (testAttr?.Description ?? ""));
                            }
                            catch (Exception ex)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                // Извлекаем наше кастомное исключение из TargetInvocationException
                                var inner = ex.InnerException;
                                Console.WriteLine($"FAILED: {inner?.Message}");
                            }
                            finally
                            {
                                Console.ResetColor();
                                afterEach?.Invoke(instance, null);
                            }
                        }
                    }
                }
            }
            Console.WriteLine("\nTesting finished. Press any key...");
            Console.ReadKey();
        }
    }
}