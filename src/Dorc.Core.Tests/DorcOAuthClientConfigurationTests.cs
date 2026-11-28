using Dorc.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace Dorc.Core.Tests;

[TestClass]
public class DorcOAuthClientConfigurationTests
{
    [TestMethod]
    [DataRow(null, "dorc-api.manage")]
    [DataRow("", "dorc-api.manage")]
    [DataRow("   ", "dorc-api.manage")]
    [DataRow("custom.scope", "custom.scope")]
    public void Scope_FallsBackWhenNullOrWhitespace(string? configuredScope, string expectedScope)
    {
        var settings = new Dictionary<string, string?>
        {
            ["DorcApi:BaseUrl"] = "https://dorc.example.com",
            ["DorcApi:ClientId"] = "client-id",
            ["DorcApi:ClientSecret"] = "client-secret"
        };
        if (configuredScope != null)
        {
            settings["DorcApi:Scope"] = configuredScope;
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var oauthConfig = new DorcOAuthClientConfiguration(config);

        Assert.AreEqual("https://dorc.example.com", oauthConfig.BaseUrl);
        Assert.AreEqual("client-id", oauthConfig.ClientId);
        Assert.AreEqual("client-secret", oauthConfig.ClientSecret);
        Assert.AreEqual(expectedScope, oauthConfig.Scope);
    }
}
