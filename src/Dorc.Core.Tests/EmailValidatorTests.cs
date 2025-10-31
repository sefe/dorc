using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class EmailValidatorTests
    {
        [TestMethod]
        public void IsValidEmail_WithValidEmail_ReturnsTrue()
        {
            // Arrange
            var validEmails = new[]
            {
                "user@example.com",
                "test.user@example.com",
                "test+tag@example.co.uk",
                "test_user@sub.example.com",
                "user123@example-domain.com",
                "Test.User@Example.COM"
            };

            // Act & Assert
            foreach (var email in validEmails)
            {
                Assert.IsTrue(EmailValidator.IsValidEmail(email), $"Expected '{email}' to be valid");
            }
        }

        [TestMethod]
        public void IsValidEmail_WithInvalidEmail_ReturnsFalse()
        {
            // Arrange
            var invalidEmails = new[]
            {
                "notanemail",
                "@example.com",
                "user@",
                "user @example.com",
                "user@example .com",
                "",
                "user@@example.com",
                "user..name@example.com",
                "user@.com",
                ".user@example.com"
            };

            // Act & Assert
            foreach (var email in invalidEmails)
            {
                Assert.IsFalse(EmailValidator.IsValidEmail(email), $"Expected '{email}' to be invalid");
            }
        }

        [TestMethod]
        public void IsValidEmail_WithNull_ReturnsFalse()
        {
            // Act
            var result = EmailValidator.IsValidEmail(null);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsValidEmail_WithWhitespace_ReturnsFalse()
        {
            // Act
            var result = EmailValidator.IsValidEmail("   ");

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsValidEmail_WithUsernameOnly_ReturnsFalse()
        {
            // Arrange
            var usernameOnly = "DOMAIN\\username";

            // Act
            var result = EmailValidator.IsValidEmail(usernameOnly);

            // Assert
            Assert.IsFalse(result, "Windows domain username should not be considered a valid email");
        }
    }
}
