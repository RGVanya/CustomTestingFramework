using MyTestFramework;
using TestedApp;

namespace AppTests
{
    [TestClass]
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
            Assert.IsNull(profile); // Хакер не существует, ждем Null
        }

        // 9. IsEmpty
        [TestMethod]
        public void TestEmptySessions()
        {
            var sessions = _service.GetActiveSessions();
            Assert.IsEmpty(sessions); // Изначально сессий быть не должно
        }

        // 10. AreSame
        [TestMethod]
        public void TestConfigReference()
        {
            var config1 = _service.GetAppConfig();
            var config2 = _service.GetAppConfig();
            Assert.AreSame(config1, config2); // Должны ссылаться на одну и ту же область памяти
        }

        // Демонстрация работы контекста
        [TestMethod]
        public void TestContextUsage()
        {
            var time = _context.Get<DateTime>("LastTestTime");
            Assert.IsNotNull(time);
        }
    }
}
