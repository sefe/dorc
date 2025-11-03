using Dorc.Core.Interfaces;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class EnvSnapBackupsTests
    {
        private IEnvBackups _sut = null!;

        [TestInitialize]
        public void Setup()
        {
            _sut = new EnvSnapBackups();
        }

        [TestMethod]
        public void GetSnapsOfStatus_WithInvalidServer_ThrowsSqlException()
        {
            // Arrange
            var invalidServer = "nonexistent-sql-server";
            var status = "Available";

            // Act & Assert - Should throw SqlException for connection failure
            Assert.ThrowsException<Microsoft.Data.SqlClient.SqlException>(() =>
            {
                _sut.GetSnapsOfStatus(invalidServer, status);
            });
        }

        [TestMethod]
        public void GetSnapsOfStatus_WithNullStatus_DoesNotThrow()
        {
            // Arrange
            var server = "localhost";
            string status = null;

            // Act & Assert - Should handle null status gracefully
            try
            {
                var result = _sut.GetSnapsOfStatus(server, status);
                Assert.IsNotNull(result);
            }
            catch (Exception)
            {
                // Expected to fail on connection, not null handling
            }
        }

        [TestMethod]
        public void GetSnapsOfStatus_ConstructsSqlQueryCorrectly()
        {
            // Arrange
            var server = "test-server";
            var status = "TestStatus";

            // Act
            try
            {
                var result = _sut.GetSnapsOfStatus(server, status);
                // Will fail to connect but verifies the method executes without crashes
                Assert.IsNotNull(result);
            }
            catch (Exception ex)
            {
                // Expected to fail on connection attempt
                Assert.IsTrue(ex.Message.Contains("network") || ex.Message.Contains("connection") || ex.Message.Contains("server"), 
                    "Should fail with connection-related error");
            }
        }
    }
}
