using Dorc.Core.Connectivity;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class ConnectivityCheckerTests
    {
        private ConnectivityChecker _checker;

        [TestInitialize]
        public void Setup()
        {
            _checker = new ConnectivityChecker();
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_WithNullServerName_ReturnsFalse()
        {
            var result = await _checker.CheckServerConnectivityAsync(null);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_WithEmptyServerName_ReturnsFalse()
        {
            var result = await _checker.CheckServerConnectivityAsync(string.Empty);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_WithWhitespaceServerName_ReturnsFalse()
        {
            var result = await _checker.CheckServerConnectivityAsync("   ");
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_WithLocalhost_ReturnsTrue()
        {
            // Localhost should always be reachable in test environment
            var result = await _checker.CheckServerConnectivityAsync("localhost");
            Assert.IsTrue(result);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_WithInvalidServerName_ReturnsFalse()
        {
            // Use a server name that is unlikely to exist
            var result = await _checker.CheckServerConnectivityAsync("nonexistent-server-12345.invalid");
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task CheckDatabaseConnectivityAsync_WithNullServerName_ReturnsFalse()
        {
            var result = await _checker.CheckDatabaseConnectivityAsync(null, "testdb");
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task CheckDatabaseConnectivityAsync_WithNullDatabaseName_ReturnsFalse()
        {
            var result = await _checker.CheckDatabaseConnectivityAsync("localhost", null);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task CheckDatabaseConnectivityAsync_WithEmptyNames_ReturnsFalse()
        {
            var result = await _checker.CheckDatabaseConnectivityAsync(string.Empty, string.Empty);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task CheckDatabaseConnectivityAsync_WithInvalidConnection_ReturnsFalse()
        {
            // This should fail because the connection is invalid
            var result = await _checker.CheckDatabaseConnectivityAsync("nonexistent-server-12345.invalid", "testdb");
            Assert.IsFalse(result);
        }
    }
}
