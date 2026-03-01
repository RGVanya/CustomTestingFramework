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

        [TestMethod("Checking successful login")]
        public void TestLoginSuccess()
        {
            Assert.IsTrue(_service.Login("admin", "123"));
        }

        [TestMethod]
        [TestCase("admin", "wrong", false)]
        [TestCase("guest", "123", false)]
        [TestCase("admin", "234", true)]
        public void TestLoginParameterized(string u, string p, bool expected)
        {
            Assert.AreEqual(expected, _service.Login(u, p));
        }

        [TestMethod]
        public async Task TestTokenAsync()
        {
            var token = await _service.GetTokenAsync();
            Assert.StringContains("TOKEN", token);
            Assert.IsNotNull(token);
        }

        [TestMethod]
        public void TestException()
        {
            Assert.Throws<ArgumentException>(() => _service.CalculateDiskSpace(-1, 10));
        }

        [TestMethod]
        public void TestContextUsage()
        {
            var time = _context.Get<DateTime>("LastTestTime");
            Assert.IsNotNull(time);
        }
    }
}
