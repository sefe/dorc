using Dorc.Terraform.Catalog;

namespace Dorc.Terraform.Catalog.Tests
{
    [TestClass]
    public class CatalogInputSuggestionsTests
    {
        private static TerraformTemplateParameter Param(
            string name,
            IReadOnlyList<string>? allowedValues = null,
            string? pattern = null,
            bool sensitive = false,
            string? suggest = null)
            => new(
                Name: name,
                Type: TerraformParameterType.String,
                Required: true,
                Description: null,
                Default: null,
                AllowedValues: allowedValues,
                Pattern: pattern,
                Min: null,
                Max: null,
                Sensitive: sensitive,
                Suggest: suggest);

        private static CatalogInputSuggestions.Context Ctx(string env, bool isProd = false, string? vertical = null, string? app = null)
            => new(env, isProd, vertical, app);

        [DataTestMethod]
        [DataRow("APP QA 01", false, "qa")]
        [DataRow("FFS DV 04", false, "dv")]
        [DataRow("Endur UT 14", false, "ut")]
        [DataRow("APP PREPROD", false, "pp")]
        [DataRow("APP PP", false, "pp")]
        [DataRow("APP Prod", true, "pr")]
        [DataRow("APP DR", true, "dr")]
        [DataRow("TEVO PR 11", true, "pr")]
        [DataRow("Terraform Dev", false, "dv")]
        public void EnvironmentTier_IsDerivedFromNameTokens(string env, bool isProd, string expected)
        {
            Assert.AreEqual(expected, CatalogInputSuggestions.EnvironmentTier(Ctx(env, isProd), out var reason));
            StringAssert.Contains(reason, env);
        }

        [TestMethod]
        public void EnvironmentTier_FallsBackToProductionFlagWhenNoTokenMatches()
        {
            Assert.AreEqual("pr", CatalogInputSuggestions.EnvironmentTier(Ctx("Billing", isProd: true), out _));
            Assert.AreEqual("dv", CatalogInputSuggestions.EnvironmentTier(Ctx("Billing", isProd: false), out _));
        }

        [TestMethod]
        public void DorcEnvironment_MatchesModuleNormalisation()
        {
            // The kafka-topic module removes characters outside a-z 0-9 _ -, so
            // the suggestion must produce exactly the suffix the module will apply.
            Assert.AreEqual("appqa01", CatalogInputSuggestions.For(Param("dorc_environment"), Ctx("APP QA 01"))!.Value);
            Assert.AreEqual("dev_01", CatalogInputSuggestions.For(Param("dorc_environment"), Ctx(" Dev_01 ")) !.Value);
        }

        [TestMethod]
        public void ProductionContextSelectsProdCloudAndProtection()
        {
            var prod = Ctx("TEVO PR 11", isProd: true);
            Assert.AreEqual(CatalogInputSuggestions.ProdCloudName, CatalogInputSuggestions.For(Param("cloud_name"), prod)!.Value);
            Assert.AreEqual("true", CatalogInputSuggestions.For(Param("termination_protection"), prod)!.Value);
            Assert.AreEqual("gbl", CatalogInputSuggestions.For(Param("scope"), prod)!.Value);

            var dev = Ctx("FFS DV 04");
            Assert.AreEqual(CatalogInputSuggestions.NonProdCloudName, CatalogInputSuggestions.For(Param("cloud_name"), dev)!.Value);
            Assert.AreEqual("false", CatalogInputSuggestions.For(Param("service_termination_protection"), dev)!.Value);
            Assert.AreEqual("tst", CatalogInputSuggestions.For(Param("scope"), dev)!.Value);
        }

        [TestMethod]
        public void ProjectCodesSeedBusinessVerticalAndOrigin()
        {
            var ctx = Ctx("APP QA 01", vertical: "pw", app: "app");
            Assert.AreEqual("pw", CatalogInputSuggestions.For(Param("business_vertical"), ctx)!.Value);
            Assert.AreEqual("app", CatalogInputSuggestions.For(Param("origin"), ctx)!.Value);

            var bare = Ctx("APP QA 01");
            Assert.AreEqual(CatalogInputSuggestions.DefaultBusinessVertical, CatalogInputSuggestions.For(Param("business_vertical"), bare)!.Value);
            Assert.IsNull(CatalogInputSuggestions.For(Param("origin"), bare), "No application code: no origin suggestion rather than a guess.");
        }

        [TestMethod]
        public void SensitiveParametersAreNeverSuggested()
        {
            Assert.IsNull(CatalogInputSuggestions.For(Param("location", sensitive: true), Ctx("APP QA 01")));
            Assert.IsNull(CatalogInputSuggestions.For(Param("token", sensitive: true, suggest: CatalogInputSuggestions.Hints.Location), Ctx("APP QA 01")));
        }

        [TestMethod]
        public void SuggestionsRespectManifestConstraints()
        {
            // kafka-topic 2.1.0 only allows dv/ut/qa/pp/pr: a derived "dr" is dropped rather than offered.
            var tierParam = Param("environment_tier", allowedValues: new[] { "dv", "ut", "qa", "pp", "pr" });
            Assert.AreEqual("qa", CatalogInputSuggestions.For(tierParam, Ctx("APP QA 01"))!.Value);
            Assert.IsNull(CatalogInputSuggestions.For(tierParam, Ctx("APP DR", isProd: true)));

            Assert.IsNull(CatalogInputSuggestions.For(Param("origin", pattern: "^[a-z]{3}$"), Ctx("X", app: "ab1")));
        }

        [TestMethod]
        public void ExplicitHintOverridesNameRule()
        {
            var p = Param("region", suggest: CatalogInputSuggestions.Hints.Location);
            Assert.AreEqual(CatalogInputSuggestions.DefaultLocation, CatalogInputSuggestions.For(p, Ctx("APP QA 01"))!.Value);
            Assert.IsNull(CatalogInputSuggestions.For(Param("region"), Ctx("APP QA 01")));
            Assert.IsNull(CatalogInputSuggestions.For(Param("location", suggest: "moon_phase"), Ctx("APP QA 01")),
                "Unknown hints are inert, not a fallback to the name rule.");
        }
    }
}
