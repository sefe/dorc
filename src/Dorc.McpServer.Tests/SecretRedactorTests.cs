using System.Text.Json;
using Dorc.ApiModel;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class SecretRedactorTests
{
    [TestMethod]
    public void DorcJsonSerialization_PreservesPascalCaseAndStringEnums()
    {
        var options = DorcJsonSerialization.CreateOptions();
        var model = new RequestStatusDto { Id = 42, Status = "Running" };
        var json = JsonSerializer.Serialize(model, options);
        StringAssert.Contains(json, "\"Id\":42");
        StringAssert.Contains(json, "\"Status\":\"Running\"");
    }

    [TestMethod]
    public void LogTruncator_HandlesNullEmptyShortLongAndSmallLimits()
    {
        Assert.AreEqual(string.Empty, LogTruncator.TruncateTail(null));
        Assert.AreEqual(string.Empty, LogTruncator.TruncateTail(string.Empty));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LogTruncator.TruncateTail("abc", 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LogTruncator.TruncateTail("abc", -5));

        Assert.AreEqual("hello", LogTruncator.TruncateTail("hello", 10));
        Assert.AreEqual("hello", LogTruncator.TruncateTail("hello", 5));

        // When maxCharacters is smaller than prefix length, returns raw tail of length maxCharacters
        var tinyTruncated = LogTruncator.TruncateTail("0123456789abcdef", 5);
        Assert.AreEqual("bcdef", tinyTruncated);

        // When maxCharacters accommodates prefix + tail
        var longText = new string('A', 200) + "TAIL_END";
        var truncated = LogTruncator.TruncateTail(longText, 80);
        Assert.IsLessThanOrEqualTo(80, truncated.Length);
        StringAssert.StartsWith(truncated, "[...truncated 169 leading characters...]\n");
        StringAssert.EndsWith(truncated, "TAIL_END");
    }

    [TestMethod]
    public void SecretRedactor_RedactsAllSensitiveAndAuditPayloads()
    {
        Assert.IsFalse(SecretRedactor.IsLikelySensitiveName(null));
        Assert.IsFalse(SecretRedactor.IsLikelySensitiveName("   "));
        Assert.IsFalse(SecretRedactor.IsLikelySensitiveName("AppUrl"));
        Assert.IsTrue(SecretRedactor.IsLikelySensitiveName("DatabasePassword"));
        Assert.IsTrue(SecretRedactor.IsLikelySensitiveName("AzureClientSecret"));
        Assert.IsTrue(SecretRedactor.IsLikelySensitiveName("StorageConnectionString"));
        Assert.IsTrue(SecretRedactor.IsLikelySensitiveName("StorageSasKey"));
        Assert.IsTrue(SecretRedactor.IsLikelySensitiveName("BlobSasToken"));

        Assert.IsEmpty(SecretRedactor.RedactPropertyValues(null));

        var propValues = new List<PropertyValueDto>
        {
            new()
            {
                Id = 1,
                Value = "plain",
                Property = new PropertyApiModel { Name = "SiteName", Secure = false }
            },
            new()
            {
                Id = 2,
                Value = "top-secret",
                Property = new PropertyApiModel { Name = "NormalName", Secure = true }
            },
            new()
            {
                Id = 3,
                Value = "heuristic-secret",
                Property = new PropertyApiModel { Name = "ApiToken", Secure = false }
            },
            new()
            {
                Id = 4,
                Value = "orphan-value",
                Property = null
            }
        };

        var redactedProps = SecretRedactor.RedactPropertyValues(propValues);
        Assert.AreEqual("plain", redactedProps[0].Value);
        Assert.AreEqual(SecretRedactor.RedactedMarker, redactedProps[1].Value);
        Assert.AreEqual(SecretRedactor.RedactedMarker, redactedProps[2].Value);
        Assert.AreEqual("orphan-value", redactedProps[3].Value);

        var nullScoped = SecretRedactor.RedactScopedPropertyValues(null);
        Assert.IsEmpty(nullScoped.Items);

        var emptyScoped = SecretRedactor.RedactScopedPropertyValues(new GetScopedPropertyValuesResponseDto { Items = null });
        Assert.IsEmpty(emptyScoped.Items);

        var scoped = new GetScopedPropertyValuesResponseDto
        {
            CurrentPage = 1,
            TotalItems = 3,
            TotalPages = 1,
            Items =
            [
                new FlatPropertyValueApiModel
                {
                    PropertyId = 10,
                    Property = "PublicSetting",
                    Secure = false,
                    PropertyValue = "visible",
                    PropertyArrayValue = ["a", "b"]
                },
                new FlatPropertyValueApiModel
                {
                    PropertyId = 11,
                    Property = "FlaggedSecret",
                    Secure = true,
                    PropertyValue = "secret1",
                    PropertyArrayValue = ["s1", "s2"]
                },
                new FlatPropertyValueApiModel
                {
                    PropertyId = 12,
                    Property = "DbPassword",
                    Secure = false,
                    PropertyValue = null,
                    PropertyArrayValue = null
                }
            ]
        };

        var redactedScoped = SecretRedactor.RedactScopedPropertyValues(scoped);
        Assert.AreEqual("visible", redactedScoped.Items[0].PropertyValue);
        CollectionAssert.AreEqual(new[] { "a", "b" }, redactedScoped.Items[0].PropertyArrayValue);
        Assert.AreEqual(SecretRedactor.RedactedMarker, redactedScoped.Items[1].PropertyValue);
        CollectionAssert.AreEqual(new[] { SecretRedactor.RedactedMarker }, redactedScoped.Items[1].PropertyArrayValue);
        Assert.IsNull(redactedScoped.Items[2].PropertyValue);
        Assert.IsNull(redactedScoped.Items[2].PropertyArrayValue);

        Assert.IsEmpty(SecretRedactor.RedactScopeOptions(null));
        var scopeOpts = SecretRedactor.RedactScopeOptions(
        [
            new PropertyValueScopeOptionApiModel { ValueOption = "Env1", SampleResolvedValue = "secret-sample" },
            new PropertyValueScopeOptionApiModel { ValueOption = "Env2", SampleResolvedValue = null }
        ]);
        Assert.AreEqual(SecretRedactor.RedactedMarker, scopeOpts[0].SampleResolvedValue);
        Assert.IsNull(scopeOpts[1].SampleResolvedValue);

        Assert.IsEmpty(SecretRedactor.RedactPropertyValuesAudit(null).Items);
        Assert.IsEmpty(SecretRedactor.RedactPropertyValuesAudit(new GetPropertyValuesAuditListResponseDto { Items = null }).Items);

        var audit = SecretRedactor.RedactPropertyValuesAudit(new GetPropertyValuesAuditListResponseDto
        {
            CurrentPage = 1,
            TotalItems = 2,
            TotalPages = 1,
            Items =
            [
                new PropertyValueAuditApiModel { Id = 1, PropertyName = "P1", FromValue = "old", ToValue = "new" },
                new PropertyValueAuditApiModel { Id = 2, PropertyName = "P2", FromValue = null, ToValue = null }
            ]
        });
        Assert.AreEqual(SecretRedactor.RedactedMarker, audit.Items[0].FromValue);
        Assert.AreEqual(SecretRedactor.RedactedMarker, audit.Items[0].ToValue);
        Assert.IsNull(audit.Items[1].FromValue);
        Assert.IsNull(audit.Items[1].ToValue);

        // Bundled requests redaction
        Assert.IsEmpty(SecretRedactor.RedactBundledRequests(null));
        Assert.IsNull(SecretRedactor.RedactBundledRequestJson(null));
        Assert.AreEqual("   ", SecretRedactor.RedactBundledRequestJson("   "));
        Assert.AreEqual(SecretRedactor.RedactedMarker, SecretRedactor.RedactBundledRequestJson("null"));
        Assert.AreEqual(SecretRedactor.RedactedMarker, SecretRedactor.RedactBundledRequestJson("[1,2,3]"));
        Assert.AreEqual(SecretRedactor.RedactedMarker, SecretRedactor.RedactBundledRequestJson("{not-valid-json"));

        var reqDtoWithNullPropsJson = "{\"Project\":\"P\",\"RequestProperties\":null}";
        var redactedNullProps = SecretRedactor.RedactBundledRequestJson(reqDtoWithNullPropsJson);
        StringAssert.Contains(redactedNullProps, "\"Project\":\"P\"");

        var copyEnvBuildJson = "{\"SourceEnvironmentName\":\"PROD-01\",\"ProjectName\":\"Proj1\",\"Components\":[10,20]}";
        var redactedCopyEnvBuild = SecretRedactor.RedactBundledRequestJson(copyEnvBuildJson);
        StringAssert.Contains(redactedCopyEnvBuild, "\"SourceEnvironmentName\":\"PROD-01\"");
        StringAssert.Contains(redactedCopyEnvBuild, "\"ProjectName\":\"Proj1\"");
        StringAssert.Contains(redactedCopyEnvBuild, "[10,20]");

        var edgeCasesPropsJson = "{\"RequestProperties\":[\"not-an-object\",{\"OtherProp\":123},{\"PropertyName\":null,\"PropertyValue\":\"null-name-sec\"},{\"PropertyName\":123,\"PropertyValue\":\"num-name-sec\"},{\"PropertyName\":\"ApiSecret\"},{\"PropertyName\":\"SafeName\",\"propertyname\":\"ApiSecret\",\"PropertyValue\":\"conflict-name-sec\"},{\"IsSensitive\":false,\"issensitive\":true,\"PropertyName\":\"SafeName\",\"PropertyValue\":\"conflict-flag-sec\"},{\"IsSensitive\":null,\"PropertyName\":\"SafeName\",\"PropertyValue\":\"null-flag-pub\"},{\"PropertyName\":\"StringFlag\",\"PropertyValue\":\"sec-str\",\"propertyvalue\":\"dup-sec-str\",\"IsSensitive\":\"true\"},{\"PropertyName\":\"FalseStrFlag\",\"PropertyValue\":\"pub-str\",\"IsSensitive\":\"false\"}]}";
        var redactedEdgeCases = SecretRedactor.RedactBundledRequestJson(edgeCasesPropsJson);
        Assert.IsFalse(redactedEdgeCases!.Contains("sec-str", StringComparison.Ordinal));
        Assert.IsFalse(redactedEdgeCases.Contains("dup-sec-str", StringComparison.Ordinal));
        Assert.IsFalse(redactedEdgeCases.Contains("num-name-sec", StringComparison.Ordinal));
        Assert.IsFalse(redactedEdgeCases.Contains("null-name-sec", StringComparison.Ordinal));
        Assert.IsFalse(redactedEdgeCases.Contains("conflict-name-sec", StringComparison.Ordinal));
        Assert.IsFalse(redactedEdgeCases.Contains("conflict-flag-sec", StringComparison.Ordinal));
        StringAssert.Contains(redactedEdgeCases, "null-flag-pub");
        StringAssert.Contains(redactedEdgeCases, "pub-str");

        var bundleRequestDto = new RequestDto
        {
            Project = "Proj1",
            Environment = "DEV",
            RequestProperties =
            [
                new RequestProperty { PropertyName = "PublicUrl", PropertyValue = "https://example.com", IsSensitive = false },
                new RequestProperty { PropertyName = "FlaggedKey", PropertyValue = "flag-secret", IsSensitive = true },
                new RequestProperty { PropertyName = "DbPassword", PropertyValue = "name-secret", IsSensitive = false }
            ]
        };
        var bundleJson = JsonSerializer.Serialize(bundleRequestDto, DorcJsonSerialization.CreateOptions());
        var redactedBundles = SecretRedactor.RedactBundledRequests(
        [
            new BundledRequestsApiModel
            {
                Id = 9,
                BundleName = "B1",
                ProjectId = 1,
                Type = BundledRequestType.JobRequest,
                RequestName = "R1",
                Sequence = 1,
                Request = bundleJson
            }
        ]);
        Assert.HasCount(1, redactedBundles);
        StringAssert.Contains(redactedBundles[0].Request, "https://example.com");
        Assert.IsFalse(redactedBundles[0].Request.Contains("flag-secret", StringComparison.Ordinal));
        Assert.IsFalse(redactedBundles[0].Request.Contains("name-secret", StringComparison.Ordinal));
        StringAssert.Contains(redactedBundles[0].Request, SecretRedactor.RedactedMarker);

        // RequestDetails XML redaction
        Assert.IsNull(SecretRedactor.RedactRequestDetailsXml(null));
        Assert.AreEqual("   ", SecretRedactor.RedactRequestDetailsXml("   "));
        Assert.AreEqual(SecretRedactor.RedactedMarker, SecretRedactor.RedactRequestDetailsXml("<unclosed"));

        var xmlPayload =
            "<DeploymentRequestDetail><Properties>" +
            "<PropertyPair><Name>PublicSetting</Name><Value>visible-val</Value><IsSensitive>false</IsSensitive></PropertyPair>" +
            "<PropertyPair><Name>FlaggedProp</Name><Value>flagged-secret</Value><IsSensitive>true</IsSensitive></PropertyPair>" +
            "<PropertyPair><Name>DatabasePassword</Name><Value>legacy-unflagged-secret</Value></PropertyPair>" +
            "<PropertyPair><Name>PasswordNoValue</Name><IsSensitive>true</IsSensitive></PropertyPair>" +
            "<PropertyPair><Value>orphan-val</Value></PropertyPair>" +
            "</Properties></DeploymentRequestDetail>";
        var redactedXml = SecretRedactor.RedactRequestDetailsXml(xmlPayload);
        StringAssert.Contains(redactedXml, "visible-val");
        Assert.IsFalse(redactedXml!.Contains("flagged-secret", StringComparison.Ordinal));
        Assert.IsFalse(redactedXml.Contains("legacy-unflagged-secret", StringComparison.Ordinal));
        StringAssert.Contains(redactedXml, SecretRedactor.RedactedMarker);
    }
}
