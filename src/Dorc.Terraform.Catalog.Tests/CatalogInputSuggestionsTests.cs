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

            // Ephemeral environments with lossy normalization append a deterministic collision-resistant suffix
            var ephClean = CatalogInputSuggestions.For(
                Param("dorc_environment"),
                new CatalogInputSuggestions.Context("PR-123", false, "tr", "app", IsEphemeral: true))!.Value;
            var ephSpace = CatalogInputSuggestions.For(
                Param("dorc_environment"),
                new CatalogInputSuggestions.Context("pr 123", false, "tr", "app", IsEphemeral: true))!.Value;
            var ephCompact = CatalogInputSuggestions.For(
                Param("dorc_environment"),
                new CatalogInputSuggestions.Context("pr123", false, "tr", "app", IsEphemeral: true))!.Value;
            var ephPreHashed = CatalogInputSuggestions.For(
                Param("dorc_environment"),
                new CatalogInputSuggestions.Context(ephSpace, false, "tr", "app", IsEphemeral: true))!.Value;
            Assert.AreEqual("pr-123", ephClean);
            Assert.AreEqual("pr123", ephCompact);
            Assert.AreNotEqual(ephCompact, ephSpace);
            Assert.AreNotEqual(ephSpace, ephPreHashed);
            StringAssert.StartsWith(ephSpace, "pr123-");
            StringAssert.StartsWith(ephPreHashed, ephSpace + "-");
            Assert.AreEqual(string.Empty, CatalogInputSuggestions.FormatDorcEnvironmentSlug("   ", isEphemeral: true));
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

        [TestMethod]
        public void EnvironmentTier_SupportsEphemeralTokensOverridesAndFlags()
        {
            foreach (var envName in new[] { "APP EPH 01", "APP EPHEMERAL 02", "APP TEMP 03", "APP TEMPORARY 04" })
            {
                Assert.AreEqual("ephemeral", CatalogInputSuggestions.EnvironmentTier(Ctx(envName), out var tokenReason));
                StringAssert.Contains(tokenReason, envName);
            }

            // Production environment ignores ephemeral token and falls back to pr.
            Assert.AreEqual("pr", CatalogInputSuggestions.EnvironmentTier(Ctx("APP EPH PROD", isProd: true), out _));
            Assert.AreEqual("pr", CatalogInputSuggestions.EnvironmentTier(Ctx("APP EPH 99", isProd: true), out _));

            // IsEphemeral flag without tier token in name resolves to ephemeral when non-prod.
            var ephFlagged = new CatalogInputSuggestions.Context("PR-4821", false, "tr", "app", IsEphemeral: true);
            Assert.AreEqual("ephemeral", CatalogInputSuggestions.EnvironmentTier(ephFlagged, out var flagReason));
            StringAssert.Contains(flagReason, "ephemeral");

            // IsEphemeral with higher non-prod token in name preserves token tier.
            var ephQa = new CatalogInputSuggestions.Context("APP QA PR-4821", false, "tr", "app", IsEphemeral: true);
            Assert.AreEqual("qa", CatalogInputSuggestions.EnvironmentTier(ephQa, out _));

            // Explicit tier override takes precedence when valid.
            var overridePp = new CatalogInputSuggestions.Context("PR-4821", false, "tr", "app", IsEphemeral: true, ExplicitTierOverride: " preprod ");
            Assert.AreEqual("pp", CatalogInputSuggestions.EnvironmentTier(overridePp, out var overrideReason));
            StringAssert.Contains(overrideReason, "preprod");

            // Explicit ephemeral override on prod environment is rejected and falls back to pr.
            var invalidProdOverride = new CatalogInputSuggestions.Context("Billing", true, "tr", "app", IsEphemeral: false, ExplicitTierOverride: "eph");
            Assert.AreEqual("pr", CatalogInputSuggestions.EnvironmentTier(invalidProdOverride, out var prodOverrideReason));
            StringAssert.Contains(prodOverrideReason, "falling back to \"pr\"");

            // Unknown explicit tier override falls back to normal token/flag detection.
            var unknownOverride = new CatalogInputSuggestions.Context("APP UT 01", false, "tr", "app", ExplicitTierOverride: "unknown_tier");
            Assert.AreEqual("ut", CatalogInputSuggestions.EnvironmentTier(unknownOverride, out _));
        }

        [TestMethod]
        public void EphemeralContext_ForcesTerminationProtectionFalse_EvenOnHigherTiersOrProd()
        {
            var ephPp = new CatalogInputSuggestions.Context(
                "PR-900", false, "tr", "app", IsEphemeral: true, ExplicitTierOverride: "pp");
            Assert.AreEqual("false", CatalogInputSuggestions.For(Param("termination_protection"), ephPp)!.Value);
            Assert.AreEqual("false", CatalogInputSuggestions.For(Param("service_termination_protection"), ephPp)!.Value);

            var ephProd = new CatalogInputSuggestions.Context(
                "PROD-EPH", true, "tr", "app", IsEphemeral: true);
            Assert.AreEqual("false", CatalogInputSuggestions.For(Param("termination_protection"), ephProd)!.Value);
            Assert.AreEqual("false", CatalogInputSuggestions.For(Param("service_termination_protection"), ephProd)!.Value);

            // Non-ephemeral context on ephemeral tier (from token) also disables termination protection and uses tst scope.
            var tokenEph = Ctx("APP EPH 01");
            Assert.AreEqual("false", CatalogInputSuggestions.For(Param("termination_protection"), tokenEph)!.Value);
            Assert.AreEqual("tst", CatalogInputSuggestions.For(Param("scope"), tokenEph)!.Value);
        }

        [TestMethod]
        public void ResolveTierWeightedInputs_PostgresAndClickHouseProfiles()
        {
            var pgManifest = new TerraformTemplateManifest(
                Name: "postgresql-database",
                Version: "2.0.0",
                Source: new TerraformTemplateSource("git", "https://example/repo", "v2.0.0"),
                Parameters: new[]
                {
                    Param("create_service"),
                    Param("database_name"),
                    Param("plan"),
                    Param("termination_protection"),
                    Param("service_termination_protection"),
                    Param("admin_password", sensitive: true),
                    Param("unmatched_with_default") with { Default = "default-val" },
                    Param("unmatched_no_default"),
                },
                Outputs: Array.Empty<TerraformTemplateOutput>(),
                Description: null,
                Tags: Array.Empty<string>(),
                Category: null,
                RequiredProviders: new Dictionary<string, string>(),
                RequiredTerraformVersion: ">= 1.5.0",
                Owner: null,
                Deprecated: false,
                DeprecationReason: null);

            var ephCtx = new CatalogInputSuggestions.Context("PR-123", false, "tr", "Endur", IsEphemeral: true);
            var ephInputs = CatalogInputSuggestions.ResolveTierWeightedInputs(pgManifest, ephCtx);
            Assert.AreEqual("false", ephInputs["create_service"]);
            Assert.AreEqual("endur_pr_123", ephInputs["database_name"]);
            Assert.AreEqual("startup-4", ephInputs["plan"]);
            Assert.AreEqual("false", ephInputs["termination_protection"]);
            Assert.AreEqual("false", ephInputs["service_termination_protection"]);
            Assert.AreEqual("default-val", ephInputs["unmatched_with_default"]);
            Assert.IsFalse(ephInputs.ContainsKey("admin_password"));
            Assert.IsFalse(ephInputs.ContainsKey("unmatched_no_default"));

            var qaCtx = new CatalogInputSuggestions.Context("APP QA 01", false, "tr", "Endur");
            var qaInputs = CatalogInputSuggestions.ResolveTierWeightedInputs(pgManifest, qaCtx);
            Assert.AreEqual("true", qaInputs["create_service"]);
            Assert.AreEqual("startup-4", qaInputs["plan"]);
            Assert.AreEqual("true", qaInputs["termination_protection"]);

            var utCtx = new CatalogInputSuggestions.Context("APP UT 01", false, "tr", "Endur");
            var utInputs = CatalogInputSuggestions.ResolveTierWeightedInputs(pgManifest, utCtx);
            Assert.AreEqual("true", utInputs["create_service"]);
            Assert.AreEqual("startup-16", utInputs["plan"]);

            var ppCtx = new CatalogInputSuggestions.Context("APP PP 01", false, "tr", "Endur", IsEphemeral: true);
            var ppInputs = CatalogInputSuggestions.ResolveTierWeightedInputs(pgManifest, ppCtx);
            Assert.AreEqual("true", ppInputs["create_service"]);
            Assert.AreEqual("business-8", ppInputs["plan"]);
            Assert.AreEqual("false", ppInputs["termination_protection"]);

            var chManifest = pgManifest with { Name = "clickhouse-database" };
            var chPpInputs = CatalogInputSuggestions.ResolveTierWeightedInputs(chManifest, ppCtx);
            Assert.AreEqual("business-16", chPpInputs["plan"]);
        }

        [TestMethod]
        public void ResolveTierWeightedInputs_KafkaTopicProfiles()
        {
            var kafkaManifest = new TerraformTemplateManifest(
                Name: "kafka-topic",
                Version: "2.1.0",
                Source: new TerraformTemplateSource("git", "https://example/repo", "v2.1.0"),
                Parameters: new[]
                {
                    Param("service_name"),
                    Param("scope"),
                    Param("partitions"),
                    Param("retention_ms"),
                    Param("termination_protection"),
                },
                Outputs: Array.Empty<TerraformTemplateOutput>(),
                Description: null,
                Tags: Array.Empty<string>(),
                Category: null,
                RequiredProviders: new Dictionary<string, string>(),
                RequiredTerraformVersion: ">= 1.5.0",
                Owner: null,
                Deprecated: false,
                DeprecationReason: null);

            var dvInputs = CatalogInputSuggestions.ResolveTierWeightedInputs(kafkaManifest, Ctx("FFS DV 04"));
            Assert.AreEqual("traveler-unstable-dev", dvInputs["service_name"]);
            Assert.AreEqual("tst", dvInputs["scope"]);
            Assert.AreEqual("1", dvInputs["partitions"]);
            Assert.AreEqual("3600000", dvInputs["retention_ms"]);
            Assert.AreEqual("false", dvInputs["termination_protection"]);

            var qaInputs = CatalogInputSuggestions.ResolveTierWeightedInputs(
                kafkaManifest,
                new CatalogInputSuggestions.Context("APP QA 01", false, "tr", "app", IsEphemeral: true));
            Assert.AreEqual("traveler-non-prod", qaInputs["service_name"]);
            Assert.AreEqual("gbl", qaInputs["scope"]);
            Assert.AreEqual("3", qaInputs["partitions"]);
            Assert.AreEqual("86400000", qaInputs["retention_ms"]);
            Assert.AreEqual("false", qaInputs["termination_protection"]);

            var ppInputs = CatalogInputSuggestions.ResolveTierWeightedInputs(kafkaManifest, Ctx("APP PP 01"));
            Assert.AreEqual("traveler-non-prod", ppInputs["service_name"]);
            Assert.AreEqual("6", ppInputs["partitions"]);
            Assert.AreEqual("604800000", ppInputs["retention_ms"]);
            Assert.AreEqual("true", ppInputs["termination_protection"]);

            var prInputs = CatalogInputSuggestions.ResolveTierWeightedInputs(kafkaManifest, Ctx("APP PR 01", isProd: true));
            Assert.AreEqual("traveler-prod", prInputs["service_name"]);
            Assert.AreEqual("6", prInputs["partitions"]);
            Assert.AreEqual("604800000", prInputs["retention_ms"]);
            Assert.AreEqual("true", prInputs["termination_protection"]);

            // Non-Kafka modules (e.g. postgresql-database or empty module name) do not receive Kafka service_name suggestions
            Assert.IsNull(TierSizingProfileResolver.TrySuggestForParameter(
                Param("service_name"),
                new CatalogInputSuggestions.Context("APP QA 01", false, "tr", "app", IsEphemeral: true, ModuleName: "postgresql-database")));
            Assert.IsNull(TierSizingProfileResolver.TrySuggestForParameter(
                Param("service_name"),
                new CatalogInputSuggestions.Context("APP QA 01", false, "tr", "app", IsEphemeral: true, ModuleName: null)));
        }

        [TestMethod]
        public void ResolveTierWeightedInputs_AzureModuleProfilesAndNamingConstraints()
        {
            var cosmosManifest = new TerraformTemplateManifest(
                Name: "cosmosdb",
                Version: "1.0.0",
                Source: new TerraformTemplateSource("git", "https://example/repo", "v1.0.0"),
                Parameters: new[]
                {
                    Param("throughput"),
                    Param("sku", allowedValues: new[] { "Basic", "Standard", "Premium" }),
                    Param("create_resource_group"),
                    Param("cosmosdb_account_name"),
                },
                Outputs: Array.Empty<TerraformTemplateOutput>(),
                Description: null,
                Tags: Array.Empty<string>(),
                Category: null,
                RequiredProviders: new Dictionary<string, string>(),
                RequiredTerraformVersion: ">= 1.5.0",
                Owner: null,
                Deprecated: false,
                DeprecationReason: null);

            var ephCosmos = CatalogInputSuggestions.ResolveTierWeightedInputs(
                cosmosManifest,
                new CatalogInputSuggestions.Context("PR-42", false, "tr", "billing", IsEphemeral: true));
            Assert.AreEqual("400", ephCosmos["throughput"]);
            Assert.AreEqual("Basic", ephCosmos["sku"]);
            Assert.AreEqual("false", ephCosmos["create_resource_group"]);
            Assert.AreEqual("cosmos-billing-pr-42", ephCosmos["cosmosdb_account_name"]);

            var qaCosmos = CatalogInputSuggestions.ResolveTierWeightedInputs(
                cosmosManifest,
                new CatalogInputSuggestions.Context("APP QA 01", false, "tr", "billing"));
            Assert.AreEqual("1000", qaCosmos["throughput"]);
            Assert.AreEqual("Standard", qaCosmos["sku"]);
            Assert.AreEqual("false", qaCosmos["create_resource_group"]);

            var ppCosmos = CatalogInputSuggestions.ResolveTierWeightedInputs(
                cosmosManifest,
                new CatalogInputSuggestions.Context("APP PP 01", false, "tr", "billing"));
            Assert.AreEqual("4000", ppCosmos["throughput"]);
            Assert.AreEqual("Premium", ppCosmos["sku"]);
            Assert.AreEqual("true", ppCosmos["create_resource_group"]);

            // Storage account <= 24 lowercase alphanumeric characters and Standard fallback when Basic is not allowed.
            var storageManifest = cosmosManifest with
            {
                Name = "storage-account",
                Parameters = new[]
                {
                    Param("sku", allowedValues: new[] { "Standard", "Premium" }),
                    Param("storage_account_name"),
                }
            };
            var storageInputs = CatalogInputSuggestions.ResolveTierWeightedInputs(
                storageManifest,
                new CatalogInputSuggestions.Context("Ephemeral-Feature-Branch-12345", false, "tr", "VeryLongApplicationCode", IsEphemeral: true, ModuleName: "storage-account"));
            Assert.AreEqual("Standard", storageInputs["sku"]);
            var stName = storageInputs["storage_account_name"]!;
            Assert.IsTrue(stName.Length <= 24);
            Assert.IsTrue(stName.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)));

            // Key vault <= 24 alphanumeric/hyphen chars, no trailing hyphen.
            var kvName = TierSizingProfileResolver.FormatResourceName(
                "key-vault", "key_vault_name", "VeryLongApplicationCode", "Ephemeral-Branch-With-Hyphens---");
            Assert.IsTrue(kvName.Length <= 24);
            Assert.IsFalse(kvName.EndsWith('-'));
            StringAssert.StartsWith(kvName, "kv-");

            var kvShortEnv = TierSizingProfileResolver.FormatResourceName(
                null, "vault_name", "abcdefgh", "prefix----------");
            Assert.IsTrue(kvShortEnv.Length <= 24);
            StringAssert.StartsWith(kvShortEnv, "kv-abcdefgh-preix-");

            // Non-alphanumeric applicationCode fallback ("app") for storage-account and key-vault
            var stFallbackApp = TierSizingProfileResolver.FormatResourceName(
                "storage-account", "storage_account_name", "---", "env");
            StringAssert.StartsWith(stFallbackApp, "stappenv");
            Assert.AreNotEqual("stappenv", stFallbackApp);
            var kvFallbackApp = TierSizingProfileResolver.FormatResourceName(
                "key-vault", "key_vault_name", "---", "env");
            StringAssert.StartsWith(kvFallbackApp, "kv-app-env-");

            // Collision resistance: long environment names with identical suffixes or prefixes produce distinct names.
            var stPr89 = TierSizingProfileResolver.FormatResourceName(
                "storage-account", "storage_account_name", "app", "pullrequest-89");
            var stPr464 = TierSizingProfileResolver.FormatResourceName(
                "storage-account", "storage_account_name", "app", "pullrequest-464");
            Assert.AreNotEqual(stPr89, stPr464);
            Assert.IsTrue(stPr89.Length <= 24);
            Assert.IsTrue(stPr464.Length <= 24);

            var stAlpha = TierSizingProfileResolver.FormatResourceName(
                "storage-account", "storage_account_name", "app", "Feature-Alpha-SharedTrailingSuffix123456");
            var stBravo = TierSizingProfileResolver.FormatResourceName(
                "storage-account", "storage_account_name", "app", "Feature-Bravo-SharedTrailingSuffix123456");
            Assert.AreNotEqual(stAlpha, stBravo);
            Assert.IsTrue(stAlpha.Length <= 24);
            Assert.IsTrue(stBravo.Length <= 24);

            var kvAlpha = TierSizingProfileResolver.FormatResourceName(
                "key-vault", "key_vault_name", "app", "Feature-Alpha-SharedTrailingSuffix123456");
            var kvBravo = TierSizingProfileResolver.FormatResourceName(
                "key-vault", "key_vault_name", "app", "Feature-Bravo-SharedTrailingSuffix123456");
            Assert.AreNotEqual(kvAlpha, kvBravo);
            Assert.IsTrue(kvAlpha.Length <= 24);
            Assert.IsTrue(kvBravo.Length <= 24);

            var kvByKeyVaultParam = TierSizingProfileResolver.FormatResourceName(
                null, "key_vault_name", "app", "dv-01");
            Assert.AreEqual("kv-app-dv-01", kvByKeyVaultParam);

            // Service bus, redis, cosmos, storage, and fallback resource prefixes.
            Assert.AreEqual("cosmos-app-dv-01", TierSizingProfileResolver.FormatResourceName(null, "cosmosdb_account_name", null, "DV-01"));
            Assert.AreEqual("stappdv01", TierSizingProfileResolver.FormatResourceName(null, "storage_account_name", "app", "DV01"));
            Assert.AreEqual("sb-app-dv-01", TierSizingProfileResolver.FormatResourceName("service-bus", "namespace_name", null, "DV-01"));
            Assert.AreEqual("sb-app-dv-01", TierSizingProfileResolver.FormatResourceName(null, "namespace_name", null, "DV-01"));
            StringAssert.StartsWith(TierSizingProfileResolver.FormatResourceName(null, "servicebus_namespace_name", "!!!", "DV-01"), "sb-app-dv-01-");
            StringAssert.StartsWith(TierSizingProfileResolver.FormatResourceName("redis-cache", "redis_name", " ", "!!!"), "redis-app-env-");
            StringAssert.StartsWith(TierSizingProfileResolver.FormatResourceName("redis-cache", "redis_name", " ", "   "), "redis-app-env-");
            Assert.AreEqual("redis-app-env", TierSizingProfileResolver.FormatResourceName("redis-cache", "redis_name", " ", "env"));
            Assert.AreEqual("redis-app-dv-01", TierSizingProfileResolver.FormatResourceName(null, "redis_name", null, "DV-01"));
            Assert.AreEqual("redis-app-dv-01", TierSizingProfileResolver.FormatResourceName(null, "cache_name", null, "DV-01"));
            Assert.AreEqual("res-app-dv-01", TierSizingProfileResolver.FormatResourceName("custom-mod", "resource_name", null, "DV-01"));
            Assert.AreEqual("app_env", TierSizingProfileResolver.FormatDatabaseName(null, "env"));
            StringAssert.StartsWith(TierSizingProfileResolver.FormatDatabaseName(null, "   "), "app_env_");
            StringAssert.StartsWith(TierSizingProfileResolver.FormatDatabaseName("!!!", "---"), "app_env_");
            StringAssert.StartsWith(TierSizingProfileResolver.FormatDatabaseName("app", "___"), "app_env_");
            Assert.AreEqual("stappenv", TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "app", "env"));
            StringAssert.StartsWith(TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "app", "___"), "stappenv");
            Assert.AreNotEqual(
                TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "app", "env"),
                TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "app", "---"));
            Assert.AreNotEqual(
                TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "app", "___"),
                TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "app", "---"));
            StringAssert.StartsWith(TierSizingProfileResolver.FormatResourceName("key-vault", "key_vault_name", "app", "___"), "kv-app-env-");
            StringAssert.StartsWith(TierSizingProfileResolver.FormatResourceName("cosmosdb", "cosmosdb_account_name", "app", "___"), "cosmos-app-env-");

            // Underscore, space, hyphen, and alphanumeric collision avoidance & max length truncation across database, storage, key-vault, cosmos, service-bus
            var dbNames = new[]
            {
                TierSizingProfileResolver.FormatDatabaseName("app", "pr-42"),
                TierSizingProfileResolver.FormatDatabaseName("app", "pr_42"),
                TierSizingProfileResolver.FormatDatabaseName("app", "pr 42"),
                TierSizingProfileResolver.FormatDatabaseName("app", "pr42"),
                TierSizingProfileResolver.FormatDatabaseName("app-1", "pr-42")
            };
            CollectionAssert.AllItemsAreUnique(dbNames);

            var storageNames = new[]
            {
                TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "app", "pr42"),
                TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "app", "pr-42"),
                TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "app", "pr_42"),
                TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "app", "pr 42"),
                TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "app-x", "pr42")
            };
            CollectionAssert.AllItemsAreUnique(storageNames);

            var kvNames = new[]
            {
                TierSizingProfileResolver.FormatResourceName("key-vault", "key_vault_name", "app", "pr-42"),
                TierSizingProfileResolver.FormatResourceName("key-vault", "key_vault_name", "app", "pr_42"),
                TierSizingProfileResolver.FormatResourceName("key-vault", "key_vault_name", "app", "pr 42"),
                TierSizingProfileResolver.FormatResourceName("key-vault", "key_vault_name", "app", "pr42"),
                TierSizingProfileResolver.FormatResourceName("key-vault", "key_vault_name", "app-x", "pr-42")
            };
            CollectionAssert.AllItemsAreUnique(kvNames);

            var cosmosNames = new[]
            {
                TierSizingProfileResolver.FormatResourceName("cosmosdb", "cosmosdb_account_name", "app", "pr-42"),
                TierSizingProfileResolver.FormatResourceName("cosmosdb", "cosmosdb_account_name", "app", "pr_42"),
                TierSizingProfileResolver.FormatResourceName("cosmosdb", "cosmosdb_account_name", "app", "pr 42"),
                TierSizingProfileResolver.FormatResourceName("cosmosdb", "cosmosdb_account_name", "app", "pr42"),
                TierSizingProfileResolver.FormatResourceName("cosmosdb", "cosmosdb_account_name", "app-x", "pr-42")
            };
            CollectionAssert.AllItemsAreUnique(cosmosNames);

            // Disambiguate "env" vs "env-", "app" vs "app-", and hex-suffixed canonical tokens vs hashed lossy tokens
            Assert.AreNotEqual(
                TierSizingProfileResolver.FormatDatabaseName("app", "env"),
                TierSizingProfileResolver.FormatDatabaseName("app", "env-"));
            Assert.AreNotEqual(
                TierSizingProfileResolver.FormatDatabaseName("app", "env"),
                TierSizingProfileResolver.FormatDatabaseName("app-", "env"));
            Assert.AreNotEqual(
                TierSizingProfileResolver.FormatDatabaseName("endur", "pr_42"),
                TierSizingProfileResolver.FormatDatabaseName("endur", "pr-42-f0b4a1"));
            Assert.AreEqual(
                "endur_pr_42_g0b4a1",
                TierSizingProfileResolver.FormatDatabaseName("endur", "pr-42-g0b4a1"));
            Assert.AreEqual(
                "endur_pr_42_a_b4a1",
                TierSizingProfileResolver.FormatDatabaseName("endur", "pr-42-a-b4a1"));
            Assert.AreEqual(
                "kv-app-pr-42-a-b4",
                TierSizingProfileResolver.FormatResourceName("key-vault", "key_vault_name", "app", "pr-42-a-b4"));
            Assert.AreNotEqual(
                TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "endur", "pr-42"),
                TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "endur", "pr42f0b4a1"));
            Assert.AreNotEqual(
                TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "app", "env"),
                TierSizingProfileResolver.FormatResourceName("storage-account", "storage_account_name", "app", "env-"));
            Assert.AreNotEqual(
                TierSizingProfileResolver.FormatResourceName("key-vault", "key_vault_name", "app", "pr_42"),
                TierSizingProfileResolver.FormatResourceName("key-vault", "key_vault_name", "app", "pr-42-f0b4a1"));
            Assert.AreNotEqual(
                TierSizingProfileResolver.FormatResourceName("cosmosdb", "cosmosdb_account_name", "app", "pr_42"),
                TierSizingProfileResolver.FormatResourceName("cosmosdb", "cosmosdb_account_name", "app", "pr-42-f0b4a1"));

            var longDb = TierSizingProfileResolver.FormatDatabaseName(
                new string('a', 40),
                new string('b', 40));
            Assert.IsTrue(longDb.Length <= 63);

            var longCosmos = TierSizingProfileResolver.FormatResourceName(
                "cosmosdb", "cosmosdb_account_name", "VeryLongApplicationCode12345", new string('e', 50));
            Assert.IsTrue(longCosmos.Length <= 44);
        }

        [TestMethod]
        public void ResolveTierWeightedInputs_RespectsOverridesExceptEphemeralProtectionAndValidatesNulls()
        {
            var manifest = new TerraformTemplateManifest(
                Name: "postgresql-database",
                Version: "2.0.0",
                Source: new TerraformTemplateSource("git", "https://example/repo", "v2.0.0"),
                Parameters: new[]
                {
                    Param("plan"),
                    Param("database_name"),
                    Param("termination_protection"),
                },
                Outputs: Array.Empty<TerraformTemplateOutput>(),
                Description: null,
                Tags: Array.Empty<string>(),
                Category: null,
                RequiredProviders: new Dictionary<string, string>(),
                RequiredTerraformVersion: ">= 1.5.0",
                Owner: null,
                Deprecated: false,
                DeprecationReason: null);

            var overrides = new Dictionary<string, string?>
            {
                ["plan"] = "business-4",
                ["database_name"] = "",
                ["termination_protection"] = "true",
            };
            var ctx = new CatalogInputSuggestions.Context("PR-77", false, "tr", "app", IsEphemeral: true);
            var resolved = CatalogInputSuggestions.ResolveTierWeightedInputs(manifest, ctx, overrides);

            Assert.AreEqual("business-4", resolved["plan"]);
            Assert.AreEqual("app_pr_77", resolved["database_name"], "Empty override falls back to suggestion.");
            Assert.AreEqual("false", resolved["termination_protection"], "Ephemeral invariant overrides termination_protection=true.");

            // Component-qualified overrides and component-disambiguated resource/database names
            var compOverrides = new Dictionary<string, string?>
            {
                ["Orders-Db:plan"] = "business-8",
                ["plan"] = "startup-4"
            };
            var ctxWithComp = new CatalogInputSuggestions.Context("PR-77", false, "tr", "app", IsEphemeral: true, ComponentName: "Orders-Db");
            var ctxWithCompNoApp = new CatalogInputSuggestions.Context("PR-77", false, "tr", null, IsEphemeral: true, ComponentName: "Billing-Db");
            var resolvedComp = CatalogInputSuggestions.ResolveTierWeightedInputs(manifest, ctxWithComp, compOverrides);
            var resolvedCompNoApp = CatalogInputSuggestions.ResolveTierWeightedInputs(manifest, ctxWithCompNoApp, null);
            Assert.AreEqual("business-8", resolvedComp["plan"]);
            Assert.AreNotEqual(resolved["database_name"], resolvedComp["database_name"]);
            Assert.AreNotEqual(resolvedComp["database_name"], resolvedCompNoApp["database_name"]);

            // Candidate list not in AllowedValues falls back to primary candidate in SuggestSku.
            var skuParam = Param("sku", allowedValues: new[] { "CustomSku" });
            Assert.AreEqual("Standard", TierSizingProfileResolver.SuggestSku(skuParam, ctx with { ModuleName = "key-vault" }).Value);

            // Null or empty AllowedValues in SuggestSku returns preferred SKU without throwing.
            Assert.AreEqual("Basic", TierSizingProfileResolver.SuggestSku(Param("sku", allowedValues: null), ctx).Value);
            Assert.AreEqual("Basic", TierSizingProfileResolver.SuggestSku(Param("sku", allowedValues: Array.Empty<string>()), ctx).Value);

            // Non-ephemeral general context does not invoke tier-sizing rules unless IsEphemeral, ModuleName, or ExplicitTierOverride is set.
            Assert.IsNull(CatalogInputSuggestions.For(Param("plan"), Ctx("APP QA 01")));
            Assert.IsNotNull(CatalogInputSuggestions.For(Param("plan"), Ctx("APP QA 01") with { IsEphemeral = true }));
            Assert.IsNull(CatalogInputSuggestions.For(Param("foo", suggest: "unknown-rule"), Ctx("APP QA 01") with { IsEphemeral = true }));
            Assert.IsNotNull(CatalogInputSuggestions.For(Param("plan"), Ctx("APP QA 01") with { ModuleName = "postgresql-database" }));
            Assert.IsNotNull(CatalogInputSuggestions.For(Param("plan"), Ctx("APP QA 01") with { ExplicitTierOverride = "qa" }));
            Assert.IsNull(CatalogInputSuggestions.For(Param("my_plan", suggest: CatalogInputSuggestions.Hints.Plan), Ctx("APP QA 01")));
            Assert.IsNotNull(CatalogInputSuggestions.For(Param("my_plan", suggest: CatalogInputSuggestions.Hints.Plan), Ctx("APP QA 01") with { IsEphemeral = true }));
            Assert.IsNotNull(CatalogInputSuggestions.For(Param("my_plan", suggest: CatalogInputSuggestions.Hints.Plan), Ctx("APP QA 01") with { ModuleName = "postgresql-database" }));
            Assert.IsNotNull(CatalogInputSuggestions.For(Param("my_plan", suggest: CatalogInputSuggestions.Hints.Plan), Ctx("APP QA 01") with { ExplicitTierOverride = "qa" }));

            Assert.ThrowsExactly<ArgumentNullException>(() =>
                TierSizingProfileResolver.ResolveTierWeightedInputs(null!, ctx));
            Assert.ThrowsExactly<ArgumentNullException>(() =>
                TierSizingProfileResolver.ResolveTierWeightedInputs(manifest, null!));
        }

        [TestMethod]
        public void HintsIsKnownHintAndPatternEdgeCases_AreCovered()
        {
            Assert.IsFalse(CatalogInputSuggestions.IsKnownHint(null));
            Assert.IsFalse(CatalogInputSuggestions.IsKnownHint("unknown_hint"));
            foreach (var hint in CatalogInputSuggestions.Hints.All)
            {
                Assert.IsTrue(CatalogInputSuggestions.IsKnownHint(hint));
            }

            // is_prod rule in both prod and non-prod contexts.
            Assert.AreEqual("true", CatalogInputSuggestions.For(Param("is_prod"), Ctx("APP PR 01", isProd: true))!.Value);
            Assert.AreEqual("false", CatalogInputSuggestions.For(Param("is_prod"), Ctx("APP QA 01", isProd: false))!.Value);

            // Empty normalized environment name produces null dorc_environment suggestion.
            Assert.IsNull(CatalogInputSuggestions.For(Param("dorc_environment"), Ctx("!!!")));

            // Matching pattern, invalid regex pattern, and timeout regex pattern.
            Assert.AreEqual("uksouth", CatalogInputSuggestions.For(Param("location", pattern: "^[a-z]+$"), Ctx("APP QA 01"))!.Value);
            Assert.IsNull(CatalogInputSuggestions.For(Param("location", pattern: "[invalid"), Ctx("APP QA 01")));
            var longApp = new string('a', 60) + "!";
            Assert.IsNull(CatalogInputSuggestions.For(Param("origin", pattern: "^(a+)+$"), Ctx("APP QA 01", app: longApp)));
        }
    }
}
