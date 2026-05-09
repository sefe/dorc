using System.Text.RegularExpressions;
using Dorc.TerraformRunner.Logging;

namespace Dorc.TerraformRunner.Tests.Logging
{
    [TestClass]
    public class SensitivePropertyRedactorTests
    {
        private static SensitivePropertyRedactor NewDefault()
            => new(SensitivePropertyRedactionOptions.Default());

        [TestMethod]
        public void RedactProperties_NoSensitiveKeys_ValuesUnchanged()
        {
            var redactor = NewDefault();
            var input = new Dictionary<string, string?>
            {
                ["a"] = "alpha",
                ["b"] = "beta",
                ["Name"] = "value",
            };

            var result = redactor.RedactProperties(input);

            Assert.AreEqual("alpha", result["a"]);
            Assert.AreEqual("beta", result["b"]);
            Assert.AreEqual("value", result["Name"]);
        }

        [TestMethod]
        public void RedactProperties_TokenKey_ValueRedacted()
        {
            var redactor = NewDefault();
            var input = new Dictionary<string, string?>
            {
                ["Terraform_Git_PAT"] = "ghp_abc123",
                ["AzureBearerToken"] = "Bearer xyz",
            };

            var result = redactor.RedactProperties(input);

            Assert.AreEqual(SensitivePropertyRedactor.RedactedMarker, result["Terraform_Git_PAT"]);
            Assert.AreEqual(SensitivePropertyRedactor.RedactedMarker, result["AzureBearerToken"]);
        }

        [TestMethod]
        public void RedactProperties_PasswordKey_CaseInsensitiveMatch()
        {
            var redactor = NewDefault();
            var input = new Dictionary<string, string?>
            {
                ["password"] = "p1",
                ["PASSWORD"] = "p2",
                ["DbPassword"] = "p3",
            };

            var result = redactor.RedactProperties(input);

            Assert.AreEqual(SensitivePropertyRedactor.RedactedMarker, result["password"]);
            Assert.AreEqual(SensitivePropertyRedactor.RedactedMarker, result["PASSWORD"]);
            Assert.AreEqual(SensitivePropertyRedactor.RedactedMarker, result["DbPassword"]);
        }

        [TestMethod]
        public void RedactProperties_CustomPatternsOnly_DefaultsNotApplied()
        {
            var customOnly = new SensitivePropertyRedactionOptions(
                new[] { new Regex("custom_secret", RegexOptions.IgnoreCase) });
            var redactor = new SensitivePropertyRedactor(customOnly);

            var input = new Dictionary<string, string?>
            {
                ["password"] = "should NOT be redacted by custom-only options",
                ["custom_secret_value"] = "should be redacted",
            };

            var result = redactor.RedactProperties(input);

            Assert.AreEqual("should NOT be redacted by custom-only options", result["password"]);
            Assert.AreEqual(SensitivePropertyRedactor.RedactedMarker, result["custom_secret_value"]);
        }

        [TestMethod]
        public void RedactProperties_DoesNotMutateInput()
        {
            var redactor = NewDefault();
            var input = new Dictionary<string, string?>
            {
                ["Token"] = "original",
            };

            redactor.RedactProperties(input);

            Assert.AreEqual("original", input["Token"]);
        }

        [TestMethod]
        public void RedactProperties_Idempotent()
        {
            var redactor = NewDefault();
            var input = new Dictionary<string, string?>
            {
                ["a"] = "alpha",
                ["Token"] = "secret",
            };

            var once = redactor.RedactProperties(input);
            var twice = redactor.RedactProperties(once);

            CollectionAssert.AreEquivalent(once.ToList(), twice.ToList());
        }

        [TestMethod]
        public void RedactJson_FlatObject_TokenValueReplaced()
        {
            var redactor = NewDefault();
            var json = "{\"a\":\"alpha\",\"Token\":\"abc\"}";

            var result = redactor.RedactJson(json);

            StringAssert.Contains(result, "\"alpha\"");
            StringAssert.Contains(result, "[REDACTED]");
            Assert.IsFalse(result.Contains("abc", StringComparison.Ordinal),
                "redacted JSON must not contain the secret value");
        }

        [TestMethod]
        public void RedactJson_NestedObject_RedactsRecursively()
        {
            var redactor = NewDefault();
            var json = "{\"outer\":{\"AzureBearerToken\":\"deep-secret\"}}";

            var result = redactor.RedactJson(json);

            Assert.IsFalse(result.Contains("deep-secret", StringComparison.Ordinal));
            StringAssert.Contains(result, "[REDACTED]");
        }

        [TestMethod]
        public void RedactJson_NonStringValues_Untouched()
        {
            var redactor = NewDefault();
            var json = "{\"count\":7,\"flag\":true,\"absent\":null,\"name\":\"alpha\"}";

            var result = redactor.RedactJson(json);

            StringAssert.Contains(result, "7");
            StringAssert.Contains(result, "true");
            StringAssert.Contains(result, "null");
            StringAssert.Contains(result, "\"alpha\"");
        }

        [TestMethod]
        public void RedactJson_MalformedInput_ReturnsInputUnchanged()
        {
            var redactor = NewDefault();
            var malformed = "{ this is not json";

            var result = redactor.RedactJson(malformed);

            Assert.AreEqual(malformed, result);
        }

        [TestMethod]
        public void RedactJson_EmptyOrNullInput_ReturnedAsIs()
        {
            var redactor = NewDefault();

            Assert.AreEqual(string.Empty, redactor.RedactJson(string.Empty));
        }

        [TestMethod]
        public void RedactJson_ArrayWithSensitiveObject_RedactsElements()
        {
            var redactor = NewDefault();
            var json = "[{\"Token\":\"a\"},{\"Token\":\"b\"}]";

            var result = redactor.RedactJson(json);

            Assert.IsFalse(result.Contains("\"a\"", StringComparison.Ordinal));
            Assert.IsFalse(result.Contains("\"b\"", StringComparison.Ordinal));
        }

        [TestMethod]
        public void Constructor_NullOptions_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => new SensitivePropertyRedactor(null!));
        }
    }
}
