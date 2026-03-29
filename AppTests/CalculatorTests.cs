using MyTestFramework;
using TestedApp;

namespace AppTests
{
    // [ДОБАВЛЕНО] Атрибут [Parallelizable] — разрешает запуск методов этого класса параллельно.
    [TestClass]
    [Parallelizable]
    public class AuthServiceTests
    {
        private AuthService _service;
        private readonly TestContext _context;

        public AuthServiceTests(TestContext context)
        {
            _context = context;
        }

        [BeforeEach]
        public void Init()
        {
            _service = new AuthService();
            _context.Set("LastTestTime", DateTime.Now);
        }

        // 1. IsTrue
        [TestMethod("Checking successful login")]
        public void TestLoginSuccess()
        {
            Assert.IsTrue(_service.Login("admin", "123"));
        }

        // 2. IsFalse
        [TestMethod("Checking failed login")]
        public void TestLoginFail()
        {
            Assert.IsFalse(_service.Login("hacker", "0000"));
        }

        // 3. AreEqual
        [TestMethod]
        [TestCase("admin", "wrong", "wewef", false)]
        [TestCase("guest", "123", false)]
        [TestCase("guest", "123", true)]
        public void TestLoginParameterized(string u, string p, bool expected)
        {
            Assert.AreEqual(expected, _service.Login(u, p));
        }

        // 4. IsNotNull и 5. StringContains
        [TestMethod]
        public async Task TestTokenAsync()
        {
            var token = await _service.GetTokenAsync();
            Assert.IsNotNull(token);
            Assert.StringContains("TOKEN", token);
        }

        // 6. Throws
        [TestMethod]
        public void TestException()
        {
            Assert.Throws<ArgumentException>(() => _service.CalculateDiskSpace(-1, 10));
        }

        // 7. IsGreaterThan
        [TestMethod("Checking disk space calculation with different inputs")]
        [TestCase(5, 100, 400)]   // PASSED
        [TestCase(10, 50, 400)]   // PASSED
        [TestCase(2, 1000, 1500)] // PASSED
        [TestCase(3, 30, 100)]    // FAILED
        public void TestCalculationMath(int files, int size, int threshold)
        {
            int space = _service.CalculateDiskSpace(files, size);
            Assert.IsGreaterThan(space, threshold);
        }

        // 8. IsNull
        [TestMethod]
        public void TestNullProfile()
        {
            string profile = _service.GetUserProfile("hacker");
            Assert.IsNull(profile);
        }

        // 9. IsEmpty
        [TestMethod]
        public void TestEmptySessions()
        {
            var sessions = _service.GetActiveSessions();
            Assert.IsEmpty(sessions);
        }

        // 10. AreSame
        [TestMethod]
        public void TestConfigReference()
        {
            var config1 = _service.GetAppConfig();
            var config2 = _service.GetAppConfig();
            Assert.AreSame(config1, config2);
        }

        // Демонстрация работы контекста
        [TestMethod]
        public void TestContextUsage()
        {
            var time = _context.Get<DateTime>("LastTestTime");
            Assert.IsNotNull(time);
        }

        // [ДОБАВЛЕНО] Медленные тесты — имитируют длительные операции (Task.Delay).
        // Именно на них видна разница между последовательным и параллельным запуском:
        // последовательно = ~4с суммарно, параллельно = ~1с (при 4 потоках).
        [TestMethod("Slow test 1 — simulating heavy I/O")]
        public async Task TestSlowOperation1()
        {
            await Task.Delay(1000);
            Assert.IsTrue(true);
        }

        [TestMethod("Slow test 2 — simulating heavy computation")]
        public async Task TestSlowOperation2()
        {
            await Task.Delay(1000);
            Assert.AreEqual(4, 2 + 2);
        }

        [TestMethod("Slow test 3 — simulating network call")]
        public async Task TestSlowOperation3()
        {
            await Task.Delay(1000);
            Assert.IsNotNull("result");
        }

        [TestMethod("Slow test 4 — simulating database query")]
        public async Task TestSlowOperation4()
        {
            await Task.Delay(1000);
            Assert.IsFalse(false);
        }

        // [ДОБАВЛЕНО] Тест с атрибутом [Timeout] — ограничение времени выполнения.
        // Этот тест завершится за 200мс, таймаут 2000мс — успеет пройти.
        [TestMethod("Test with timeout — should pass")]
        [Timeout(2000)]
        public async Task TestWithTimeoutPass()
        {
            await Task.Delay(200);
            Assert.IsTrue(true);
        }

        // [ДОБАВЛЕНО] Тест, который ПРЕВЫСИТ таймаут и будет принудительно прерван.
        // Задержка 5000мс при таймауте 500мс → статус TIMEOUT.
        [TestMethod("Test with timeout — should timeout")]
        [Timeout(500)]
        public async Task TestWithTimeoutFail()
        {
            await Task.Delay(5000);
            Assert.IsTrue(true);
        }
    }
}
