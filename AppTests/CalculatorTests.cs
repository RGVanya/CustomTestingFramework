using MyTestFramework;
using TestedApp;

namespace AppTests
{
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

        [TestMethod("Checking successful login")]
        [Category("Smoke")]
        [Priority(1)]
        [Author("Student")]
        public void TestLoginSuccess()
        {
            Assert.IsTrue(_service.Login("admin", "123"));
        }

        [TestMethod("Checking failed login")]
        [Category("Smoke")]
        [Priority(1)]
        [Author("Student")]
        public void TestLoginFail()
        {
            Assert.IsFalse(_service.Login("hacker", "0000"));
        }

        [TestMethod]
        [Category("Regression")]
        [Priority(2)]
        [Author("Student")]
        [TestCaseSource(nameof(LoginCases))]
        public void TestLoginParameterized(string u, string p, bool expected)
        {
            Assert.AreEqual(expected, _service.Login(u, p));
        }

        public static IEnumerable<object[]> LoginCases()
        {
            yield return new object[] { "admin", "wrong", false };
            yield return new object[] { "guest", "123", false };
            yield return new object[] { "guest", "123", true };
        }

        [TestMethod]
        [Category("Regression")]
        [Priority(2)]
        [Author("Student")]
        public async Task TestTokenAsync()
        {
            var token = await _service.GetTokenAsync();
            Assert.IsNotNull(token);
            Assert.StringContains("TOKEN", token);
        }

        [TestMethod]
        [Category("Smoke")]
        [Priority(1)]
        [Author("Student")]
        public void TestException()
        {
            Assert.Throws<ArgumentException>(() => _service.CalculateDiskSpace(-1, 10));
        }

        [TestMethod("Checking disk space calculation with different inputs")]
        [Category("Regression")]
        [Priority(3)]
        [Author("Student")]
        [TestCase(5, 100, 400)]
        [TestCase(10, 50, 400)]
        [TestCase(2, 1000, 1500)]
        [TestCase(3, 30, 100)]
        public void TestCalculationMath(int files, int size, int threshold)
        {
            int space = _service.CalculateDiskSpace(files, size);
            Assert.IsGreaterThan(space, threshold);
        }

        [TestMethod]
        [Category("Smoke")]
        public void TestNullProfile()
        {
            string profile = _service.GetUserProfile("hacker");
            Assert.IsNull(profile);
        }

        [TestMethod]
        [Category("Smoke")]
        public void TestEmptySessions()
        {
            var sessions = _service.GetActiveSessions();
            Assert.IsEmpty(sessions);
        }

        [TestMethod]
        [Category("Smoke")]
        public void TestConfigReference()
        {
            var config1 = _service.GetAppConfig();
            var config2 = _service.GetAppConfig();
            Assert.AreSame(config1, config2);
        }

        [TestMethod]
        [Category("Smoke")]
        public void TestContextUsage()
        {
            var time = _context.Get<DateTime>("LastTestTime");
            Assert.IsNotNull(time);
        }

        [TestMethod("Slow test 1 - simulating heavy I/O")]
        [Category("Load")]
        public async Task TestSlowOperation1()
        {
            await Task.Delay(1000);
            Assert.IsTrue(true);
        }

        [TestMethod("Slow test 2 - simulating heavy computation")]
        [Category("Load")]
        public async Task TestSlowOperation2()
        {
            await Task.Delay(1000);
            Assert.AreEqual(4, 2 + 2);
        }

        [TestMethod("Slow test 3 - simulating network call")]
        [Category("Load")]
        public async Task TestSlowOperation3()
        {
            await Task.Delay(1000);
            Assert.IsNotNull("result");
        }

        [TestMethod("Slow test 4 - simulating database query")]
        [Category("Load")]
        public async Task TestSlowOperation4()
        {
            await Task.Delay(1000);
            Assert.IsFalse(false);
        }

        [TestMethod("Test with timeout - should pass")]
        [Category("Timeout")]
        [Timeout(2000)]
        public async Task TestWithTimeoutPass()
        {
            await Task.Delay(200);
            Assert.IsTrue(true);
        }

        [TestMethod("Test with timeout - should timeout")]
        [Category("Timeout")]
        [Timeout(500)]
        public async Task TestWithTimeoutFail()
        {
            await Task.Delay(5000);
            Assert.IsTrue(true);
        }

        [TestMethod("Expression tree assert demo")]
        [Category("Regression")]
        [Priority(2)]
        public void TestExpressionAssert()
        {
            int used = _service.CalculateDiskSpace(2, 10);
            int limit = 10;
            Assert.That(() => used > limit);
        }
    }
}
