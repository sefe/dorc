using Dorc.Terraform.Catalog;

namespace Dorc.Terraform.Catalog.Tests
{
    [TestClass]
    public class ParameterValidatorTests
    {
        private readonly ParameterValidator validator = new();

        private static TerraformTemplateManifest Manifest(params TerraformTemplateParameter[] parameters)
            => new(
                Name: "vnet",
                Version: "1.0.0",
                Source: new TerraformTemplateSource("git", "https://example/repo", "v1.0.0"),
                Parameters: parameters,
                Outputs: Array.Empty<TerraformTemplateOutput>(),
                Description: null,
                Tags: Array.Empty<string>(),
                Category: null,
                RequiredProviders: new Dictionary<string, string>(),
                RequiredTerraformVersion: ">= 1.5.0",
                Owner: null,
                Deprecated: false,
                DeprecationReason: null);

        private static TerraformTemplateParameter Param(
            string name,
            TerraformParameterType type = TerraformParameterType.String,
            bool required = true,
            IReadOnlyList<string>? allowedValues = null,
            string? pattern = null,
            decimal? min = null,
            decimal? max = null,
            bool sensitive = false)
            => new(name, type, required, null, null, allowedValues, pattern, min, max, sensitive);

        [TestMethod]
        public void Validate_AllRequiredSupplied_ReturnsValid()
        {
            var manifest = Manifest(Param("name", required: true));
            var supplied = new Dictionary<string, string?> { ["name"] = "alpha" };

            var result = validator.Validate(manifest, supplied);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(0, result.Errors.Count);
        }

        [TestMethod]
        public void Validate_RequiredMissing_ReportsMissing()
        {
            var manifest = Manifest(Param("name", required: true));
            var supplied = new Dictionary<string, string?>();

            var result = validator.Validate(manifest, supplied);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(1, result.Errors.Count);
            Assert.AreEqual(ParameterValidationErrorKind.Missing, result.Errors[0].Kind);
            Assert.AreEqual("name", result.Errors[0].ParameterName);
        }

        [TestMethod]
        public void Validate_TypeMismatchOnNumber_ReportsTypeMismatch()
        {
            var manifest = Manifest(Param("count", TerraformParameterType.Number));
            var supplied = new Dictionary<string, string?> { ["count"] = "not-a-number" };

            var result = validator.Validate(manifest, supplied);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ParameterValidationErrorKind.TypeMismatch, result.Errors[0].Kind);
        }

        [TestMethod]
        public void Validate_BoolTypeAcceptsTrueFalse()
        {
            var manifest = Manifest(Param("flag", TerraformParameterType.Bool));
            foreach (var v in new[] { "true", "false", "True", "FALSE" })
            {
                var supplied = new Dictionary<string, string?> { ["flag"] = v };
                Assert.IsTrue(validator.Validate(manifest, supplied).IsValid, $"value '{v}' should be a valid bool");
            }
        }

        [TestMethod]
        public void Validate_AllowedValuesEnforced()
        {
            var manifest = Manifest(Param("env", allowedValues: new[] { "dev", "prod" }));
            var supplied = new Dictionary<string, string?> { ["env"] = "staging" };

            var result = validator.Validate(manifest, supplied);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ParameterValidationErrorKind.NotAllowed, result.Errors[0].Kind);
        }

        [TestMethod]
        public void Validate_PatternEnforced()
        {
            var manifest = Manifest(Param("server_name", pattern: "^[a-z][a-z0-9-]+$"));
            var supplied = new Dictionary<string, string?> { ["server_name"] = "BadName!" };

            var result = validator.Validate(manifest, supplied);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ParameterValidationErrorKind.PatternMismatch, result.Errors[0].Kind);
        }

        // ReDoS guard: a manifest pattern with catastrophic backtracking run
        // against a long non-matching value must yield a PatternMismatch
        // validation error instead of hanging the request thread. Depending on
        // the runtime's regex optimisations the engine either times out (the
        // 1s RegexMatchTimeout converts the hang into an error) or completes
        // with a non-match; both surface as PatternMismatch, so the assertion
        // is deliberately tolerant of which path fires.
        [TestMethod]
        public void Validate_CatastrophicBacktrackingPattern_ReportsPatternMismatchInsteadOfHanging()
        {
            var manifest = Manifest(Param("host_name", pattern: "^(a+)+$"));
            var supplied = new Dictionary<string, string?>
            {
                ["host_name"] = new string('a', 40) + "!"
            };

            var result = validator.Validate(manifest, supplied);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e =>
                e.Kind == ParameterValidationErrorKind.PatternMismatch && e.ParameterName == "host_name"),
                "A PatternMismatch error must be recorded for the pathological pattern.");
        }

        [TestMethod]
        public void Validate_NumericRangeEnforced()
        {
            var manifest = Manifest(Param("size", TerraformParameterType.Number, min: 1, max: 100));
            var supplied = new Dictionary<string, string?> { ["size"] = "200" };

            var result = validator.Validate(manifest, supplied);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ParameterValidationErrorKind.OutOfRange, result.Errors[0].Kind);
        }

        [TestMethod]
        public void Validate_UnknownParameter_Reported()
        {
            var manifest = Manifest(Param("known"));
            var supplied = new Dictionary<string, string?>
            {
                ["known"] = "ok",
                ["unknown"] = "x",
            };

            var result = validator.Validate(manifest, supplied);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e =>
                e.Kind == ParameterValidationErrorKind.UnknownParameter && e.ParameterName == "unknown"));
        }

        // ---------- Sensitive-value redaction in error messages ----------

        [TestMethod]
        public void Validate_SensitiveNumberTypeMismatch_RedactsValueInMessage()
        {
            var manifest = Manifest(Param("secret_count", TerraformParameterType.Number, sensitive: true));
            var supplied = new Dictionary<string, string?> { ["secret_count"] = "hunter2-not-a-number" };

            var result = validator.Validate(manifest, supplied);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ParameterValidationErrorKind.TypeMismatch, result.Errors[0].Kind);
            StringAssert.Contains(result.Errors[0].Message, "[REDACTED]",
                "Sensitive parameter's error message must carry the redaction marker.");
            Assert.IsFalse(result.Errors[0].Message.Contains("hunter2-not-a-number"),
                "Sensitive parameter's raw value must never appear in a validation error message.");
        }

        [TestMethod]
        public void Validate_SensitivePatternMismatch_RedactsValueInMessage()
        {
            var manifest = Manifest(Param("api_key", pattern: "^[a-z0-9]+$", sensitive: true));
            var supplied = new Dictionary<string, string?> { ["api_key"] = "Secret-Value!" };

            var result = validator.Validate(manifest, supplied);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ParameterValidationErrorKind.PatternMismatch, result.Errors[0].Kind);
            StringAssert.Contains(result.Errors[0].Message, "[REDACTED]",
                "Sensitive parameter's error message must carry the redaction marker.");
            Assert.IsFalse(result.Errors[0].Message.Contains("Secret-Value!"),
                "Sensitive parameter's raw value must never appear in a validation error message.");
        }

        [TestMethod]
        public void Validate_SensitiveNumberBelowMin_RedactsValueInMessage()
        {
            var manifest = Manifest(Param("secret_size", TerraformParameterType.Number, min: 10, sensitive: true));
            var supplied = new Dictionary<string, string?> { ["secret_size"] = "3" };

            var result = validator.Validate(manifest, supplied);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ParameterValidationErrorKind.OutOfRange, result.Errors[0].Kind);
            StringAssert.Contains(result.Errors[0].Message, "[REDACTED]",
                "Sensitive parameter's error message must carry the redaction marker.");
            Assert.IsFalse(result.Errors[0].Message.Contains("3"),
                "Sensitive parameter's raw value must never appear in a validation error message.");
        }

        [TestMethod]
        public void Validate_NonSensitivePatternMismatch_ShowsValueInMessage()
        {
            var manifest = Manifest(Param("server_name", pattern: "^[a-z][a-z0-9-]+$"));
            var supplied = new Dictionary<string, string?> { ["server_name"] = "BadName!" };

            var result = validator.Validate(manifest, supplied);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(ParameterValidationErrorKind.PatternMismatch, result.Errors[0].Kind);
            StringAssert.Contains(result.Errors[0].Message, "'BadName!'",
                "Non-sensitive parameter's value is still interpolated (quoted) for diagnosability.");
            Assert.IsFalse(result.Errors[0].Message.Contains("[REDACTED]"),
                "Non-sensitive parameters must not be redacted.");
        }

        [TestMethod]
        public void Validate_NullManifest_Throws()
        {
            Assert.ThrowsExactly<ArgumentNullException>(
                () => validator.Validate(null!, new Dictionary<string, string?>()));
        }

        [TestMethod]
        public void Validate_NullSupplied_Throws()
        {
            var manifest = Manifest();
            Assert.ThrowsExactly<ArgumentNullException>(
                () => validator.Validate(manifest, null!));
        }
    }
}
