using log4net;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class SqlUserPasswordResetTests
    {
        private ILog _logger = null!;
        private SqlUserPasswordReset _sut = null!;

        [TestInitialize]
        public void Setup()
        {
            _logger = Substitute.For<ILog>();
            _sut = new SqlUserPasswordReset(_logger);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ResetSqlUserPassword_WithInvalidUsername_ThrowsException()
        {
            // Arrange
            var invalidUsername = "user'; DROP TABLE Users; --";
            var targetDbServer = "localhost";

            // Act
            _sut.ResetSqlUserPassword(targetDbServer, invalidUsername);

            // Assert handled by ExpectedException
        }

        [TestMethod]
        public void ResetSqlUserPassword_WithValidUsername_DoesNotThrowOnConnectionFailure()
        {
            // Arrange
            var validUsername = "testUser";
            var targetDbServer = "nonexistent-server";

            // Act & Assert - Should handle connection failures gracefully
            var result = _sut.ResetSqlUserPassword(targetDbServer, validUsername);
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Result);
            _logger.Received().Error(Arg.Any<string>(), Arg.Any<Exception>());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ResetSqlUserPassword_WithSpecialCharacters_ThrowsException()
        {
            // Arrange
            var invalidUsername = "user<script>";
            var targetDbServer = "localhost";

            // Act
            _sut.ResetSqlUserPassword(targetDbServer, invalidUsername);
        }

        [TestMethod]
        public void ResetSqlUserPassword_ValidatesUsernamePattern()
        {
            // Arrange - Valid usernames with allowed characters
            var validUsernames = new[] { "user123", "user-name", "user_name", "user.name", "user name", "user(name)", "user&name" };
            var targetDbServer = "nonexistent-server";

            // Act & Assert - Should not throw ArgumentException for valid patterns
            foreach (var username in validUsernames)
            {
                var result = _sut.ResetSqlUserPassword(targetDbServer, username);
                Assert.IsNotNull(result, $"Failed for username: {username}");
            }
        }
    }
}
