using Dorc.Core.Connectivity;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class ConnectivityCheckerTests
    {
        private const int PingTimeoutMs = 5000;
        private const int TcpProbeTimeoutMs = 5000;
        private const int SmbPort = 445;
        private const int DbTimeoutSeconds = 5;

        // Seam-mocked subclass per SPEC-S-003 §2.1: the seam shape chosen is
        // `protected virtual` methods overridden in this test-only subclass.
        // Each override records the args it was called with and returns a
        // configurable result, letting tests assert both invocation and arguments.
        private sealed class FakeProbe : ConnectivityChecker
        {
            public Func<string, int, Task<bool>>? OnPing { get; set; }
            public Func<string, int, int, Task<bool>>? OnTcp { get; set; }
            public Func<string, string, int, Task<bool>>? OnSql { get; set; }

            public List<(string host, int timeoutMs)> PingCalls { get; } = new();
            public List<(string host, int port, int timeoutMs)> TcpCalls { get; } = new();
            public List<(string host, string db, int timeoutSec)> SqlCalls { get; } = new();

            protected override Task<bool> TryPingAsync(string serverName, int timeoutMs)
            {
                PingCalls.Add((serverName, timeoutMs));
                return OnPing is null ? Task.FromResult(false) : OnPing(serverName, timeoutMs);
            }

            protected override Task<bool> TryTcpConnectAsync(string serverName, int port, int timeoutMs)
            {
                TcpCalls.Add((serverName, port, timeoutMs));
                return OnTcp is null ? Task.FromResult(false) : OnTcp(serverName, port, timeoutMs);
            }

            protected override Task<bool> TryOpenSqlConnectionAsync(string serverName, string databaseName, int timeoutSeconds)
            {
                SqlCalls.Add((serverName, databaseName, timeoutSeconds));
                return OnSql is null ? Task.FromResult(false) : OnSql(serverName, databaseName, timeoutSeconds);
            }
        }

        // ----- §3.1 server-probe tests -----

        [TestMethod]
        public async Task CheckServerConnectivityAsync_NullName_ReturnsFalse_NoProbe()
        {
            var fake = new FakeProbe();
            var result = await fake.CheckServerConnectivityAsync(null!);
            Assert.IsFalse(result);
            Assert.AreEqual(0, fake.PingCalls.Count);
            Assert.AreEqual(0, fake.TcpCalls.Count);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_EmptyName_ReturnsFalse_NoProbe()
        {
            var fake = new FakeProbe();
            var result = await fake.CheckServerConnectivityAsync(string.Empty);
            Assert.IsFalse(result);
            Assert.AreEqual(0, fake.PingCalls.Count);
            Assert.AreEqual(0, fake.TcpCalls.Count);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_WhitespaceName_ReturnsFalse_NoProbe()
        {
            var fake = new FakeProbe();
            var result = await fake.CheckServerConnectivityAsync("   ");
            Assert.IsFalse(result);
            Assert.AreEqual(0, fake.PingCalls.Count);
            Assert.AreEqual(0, fake.TcpCalls.Count);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_PingTrue_ReturnsTrue_NoTcp()
        {
            var fake = new FakeProbe { OnPing = (_, _) => Task.FromResult(true) };
            var result = await fake.CheckServerConnectivityAsync("host01");
            Assert.IsTrue(result);
            Assert.AreEqual(1, fake.PingCalls.Count);
            Assert.AreEqual(0, fake.TcpCalls.Count);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_PingFalse_TcpTrue_ReturnsTrue()
        {
            var fake = new FakeProbe
            {
                OnPing = (_, _) => Task.FromResult(false),
                OnTcp = (_, _, _) => Task.FromResult(true),
            };
            var result = await fake.CheckServerConnectivityAsync("host01");
            Assert.IsTrue(result);
            Assert.AreEqual(1, fake.PingCalls.Count);
            Assert.AreEqual(1, fake.TcpCalls.Count);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_PingFalse_TcpFalse_ReturnsFalse()
        {
            var fake = new FakeProbe
            {
                OnPing = (_, _) => Task.FromResult(false),
                OnTcp = (_, _, _) => Task.FromResult(false),
            };
            var result = await fake.CheckServerConnectivityAsync("host01");
            Assert.IsFalse(result);
            Assert.AreEqual(1, fake.PingCalls.Count);
            Assert.AreEqual(1, fake.TcpCalls.Count);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_PingThrows_TcpTrue_ReturnsTrue()
        {
            var fake = new FakeProbe
            {
                OnPing = (_, _) => throw new InvalidOperationException("ping boom"),
                OnTcp = (_, _, _) => Task.FromResult(true),
            };
            var result = await fake.CheckServerConnectivityAsync("host01");
            Assert.IsTrue(result);
            Assert.AreEqual(1, fake.PingCalls.Count);
            Assert.AreEqual(1, fake.TcpCalls.Count);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_PingThrows_TcpThrows_ReturnsFalse()
        {
            var fake = new FakeProbe
            {
                OnPing = (_, _) => throw new InvalidOperationException("ping boom"),
                OnTcp = (_, _, _) => throw new InvalidOperationException("tcp boom"),
            };
            var result = await fake.CheckServerConnectivityAsync("host01");
            Assert.IsFalse(result);
            Assert.AreEqual(1, fake.PingCalls.Count);
            Assert.AreEqual(1, fake.TcpCalls.Count);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_HostnameForwardedVerbatim_ToBothProbes()
        {
            var fake = new FakeProbe
            {
                OnPing = (_, _) => Task.FromResult(false),
                OnTcp = (_, _, _) => Task.FromResult(false),
            };
            const string host = "MyServer.example.local";
            await fake.CheckServerConnectivityAsync(host);
            Assert.AreEqual(host, fake.PingCalls.Single().host);
            Assert.AreEqual(host, fake.TcpCalls.Single().host);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_PingSeamInvokedWith_PingTimeoutMs()
        {
            var fake = new FakeProbe { OnPing = (_, _) => Task.FromResult(true) };
            await fake.CheckServerConnectivityAsync("host01");
            Assert.AreEqual(PingTimeoutMs, fake.PingCalls.Single().timeoutMs);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_TcpSeamInvokedWith_Port445_And_TcpTimeoutMs()
        {
            var fake = new FakeProbe
            {
                OnPing = (_, _) => Task.FromResult(false),
                OnTcp = (_, _, _) => Task.FromResult(false),
            };
            await fake.CheckServerConnectivityAsync("host01");
            var (_, port, timeoutMs) = fake.TcpCalls.Single();
            Assert.AreEqual(SmbPort, port);
            Assert.AreEqual(TcpProbeTimeoutMs, timeoutMs);
        }

        [TestMethod]
        public async Task CheckServerConnectivityAsync_InvocationCounts_PingOncePerCall_TcpOnlyAfterPingFailureOrThrow()
        {
            var pingTrue = new FakeProbe { OnPing = (_, _) => Task.FromResult(true) };
            await pingTrue.CheckServerConnectivityAsync("h");
            Assert.AreEqual(1, pingTrue.PingCalls.Count);
            Assert.AreEqual(0, pingTrue.TcpCalls.Count);

            var pingFalse = new FakeProbe
            {
                OnPing = (_, _) => Task.FromResult(false),
                OnTcp = (_, _, _) => Task.FromResult(true),
            };
            await pingFalse.CheckServerConnectivityAsync("h");
            Assert.AreEqual(1, pingFalse.PingCalls.Count);
            Assert.AreEqual(1, pingFalse.TcpCalls.Count);

            var pingThrows = new FakeProbe
            {
                OnPing = (_, _) => throw new Exception(),
                OnTcp = (_, _, _) => Task.FromResult(true),
            };
            await pingThrows.CheckServerConnectivityAsync("h");
            Assert.AreEqual(1, pingThrows.PingCalls.Count);
            Assert.AreEqual(1, pingThrows.TcpCalls.Count);
        }

        // ----- §3.2 db-probe tests -----

        [TestMethod]
        public async Task CheckDatabaseConnectivityAsync_NullServerName_ReturnsFalse_NoOpen()
        {
            var fake = new FakeProbe();
            var result = await fake.CheckDatabaseConnectivityAsync(null!, "testdb");
            Assert.IsFalse(result);
            Assert.AreEqual(0, fake.SqlCalls.Count);
        }

        [TestMethod]
        public async Task CheckDatabaseConnectivityAsync_NullDatabaseName_ReturnsFalse_NoOpen()
        {
            var fake = new FakeProbe();
            var result = await fake.CheckDatabaseConnectivityAsync("localhost", null!);
            Assert.IsFalse(result);
            Assert.IsEmpty(fake.SqlCalls);
        }

        [TestMethod]
        public async Task CheckDatabaseConnectivityAsync_WhitespaceNames_ReturnsFalse_NoOpen()
        {
            var fake = new FakeProbe();
            Assert.IsFalse(await fake.CheckDatabaseConnectivityAsync("   ", "testdb"));
            Assert.IsFalse(await fake.CheckDatabaseConnectivityAsync("localhost", "   "));
            Assert.IsFalse(await fake.CheckDatabaseConnectivityAsync(string.Empty, string.Empty));
            Assert.IsEmpty(fake.SqlCalls);
        }

        // §3.2 tests 4 and 5 (auth-fail / successful open) are covered via
        // the seam, not against a real SQL Server, per SPEC-S-003 §3.2 default.
        [TestMethod]
        public async Task CheckDatabaseConnectivityAsync_OpenFaults_ReturnsFalse()
        {
            var fake = new FakeProbe
            {
                OnSql = (_, _, _) => Task.FromException<bool>(new InvalidOperationException("auth-fail simulated")),
            };
            var result = await fake.CheckDatabaseConnectivityAsync("server", "db");
            Assert.IsFalse(result);
            Assert.HasCount(1, fake.SqlCalls);
        }

        [TestMethod]
        public async Task CheckDatabaseConnectivityAsync_OpenSucceeds_ReturnsTrue()
        {
            var fake = new FakeProbe
            {
                OnSql = (_, _, _) => Task.FromResult(true),
            };
            var result = await fake.CheckDatabaseConnectivityAsync("server", "db");
            Assert.IsTrue(result);
            Assert.HasCount(1, fake.SqlCalls);
            var (host, db, timeoutSec) = fake.SqlCalls.Single();
            Assert.AreEqual("server", host);
            Assert.AreEqual("db", db);
            Assert.AreEqual(DbTimeoutSeconds, timeoutSec);
        }
    }
}
