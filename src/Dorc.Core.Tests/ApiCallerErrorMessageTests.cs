using Dorc.Core;
using Microsoft.Extensions.Configuration;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class ApiCallerErrorMessageTests
    {
        [TestMethod]
        public void ExtractErrorMessage_BareJsonString_IsUnwrapped()
        {
            var result = ApiCaller.ExtractErrorMessage("\"Project name must be provided.\"");

            Assert.AreEqual("Project name must be provided.", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonStringWithEscapes_IsDecoded()
        {
            var result = ApiCaller.ExtractErrorMessage("\"line1\\nline2\"");

            Assert.AreEqual("line1\nline2", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonObjectWithMessage_ReturnsMessage()
        {
            var result = ApiCaller.ExtractErrorMessage("{\"RequestIds\":[],\"Success\":false,\"Message\":\"Environment not found\"}");

            Assert.AreEqual("Environment not found", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonObjectWithLowercaseMessage_ReturnsMessage()
        {
            var result = ApiCaller.ExtractErrorMessage("{\"message\":\"boom\"}");

            Assert.AreEqual("boom", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_ProblemDetailsWithDetail_ReturnsDetail()
        {
            var result = ApiCaller.ExtractErrorMessage("{\"title\":\"Bad Request\",\"status\":400,\"detail\":\"The build was not found\"}");

            Assert.AreEqual("The build was not found", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_ProblemDetailsWithOnlyTitle_ReturnsTitle()
        {
            var result = ApiCaller.ExtractErrorMessage("{\"title\":\"One or more validation errors occurred.\",\"status\":400}");

            Assert.AreEqual("One or more validation errors occurred.", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonObjectWithEmptyMessage_FallsBackToRawContent()
        {
            const string raw = "{\"Message\":\"\"}";

            var result = ApiCaller.ExtractErrorMessage(raw);

            Assert.AreEqual(raw, result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonObjectWithoutRecognisedProperty_FallsBackToRawContent()
        {
            const string raw = "{\"errors\":{\"BuildText\":[\"required\"]}}";

            var result = ApiCaller.ExtractErrorMessage(raw);

            Assert.AreEqual(raw, result);
        }

        [TestMethod]
        public void ExtractErrorMessage_PlainText_IsReturnedAsIs()
        {
            const string raw = "Internal server error";

            var result = ApiCaller.ExtractErrorMessage(raw);

            Assert.AreEqual(raw, result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonNumber_FallsBackToRawContent()
        {
            var result = ApiCaller.ExtractErrorMessage("42");

            Assert.AreEqual("42", result);
        }

        [TestMethod]
        [DataRow(null, "dorc-api.manage")]
        [DataRow("", "dorc-api.manage")]
        [DataRow("   ", "dorc-api.manage")]
        [DataRow("custom.scope", "custom.scope")]
        public void DorcOAuthClientConfiguration_Scope_FallsBackWhenNullOrWhitespace(string? configuredScope, string expectedScope)
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

            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            var oauthConfig = new Dorc.Core.Configuration.DorcOAuthClientConfiguration(config);

            Assert.AreEqual("https://dorc.example.com", oauthConfig.BaseUrl);
            Assert.AreEqual("client-id", oauthConfig.ClientId);
            Assert.AreEqual("client-secret", oauthConfig.ClientSecret);
            Assert.AreEqual(expectedScope, oauthConfig.Scope);
        }

        [TestMethod]
        public void ConfigurationSettings_RunnerAndRefDataGetters_ReturnConfiguredValues()
        {
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AppSettings:RefDataApiUrl"] = "https://dorc.example.com",
                    ["AppSettings:RunnerLogPath"] = "D:\\Logs\\Runner",
                    ["AppSettings:DotNetFrameworkDeploymentRunnerPath"] = "C:\\Runner\\NetFx\\Dorc.NetFramework.Runner.exe",
                    ["AppSettings:DotNetCoreDeploymentRunnerPath"] = "C:\\Runner\\NetCore\\Dorc.Runner.exe",
                    ["AppSettings:TerraformDeploymentRunnerPath"] = "C:\\Runner\\Terraform\\Dorc.TerraformRunner.exe"
                })
                .Build();

            var settings = new Dorc.Core.Configuration.ConfigurationSettings(config);

            Assert.AreEqual("https://dorc.example.com", settings.GetRefDataApiUrl());
            Assert.AreEqual("D:\\Logs\\Runner", settings.GetRunnerLogPath());
            Assert.AreEqual("C:\\Runner\\NetFx\\Dorc.NetFramework.Runner.exe", settings.GetDotNetFrameworkDeploymentRunnerPath());
            Assert.AreEqual("C:\\Runner\\NetCore\\Dorc.Runner.exe", settings.GetDotNetCoreDeploymentRunnerPath());
            Assert.AreEqual("C:\\Runner\\Terraform\\Dorc.TerraformRunner.exe", settings.GetTerraformDeploymentRunnerPath());
        }
    }
}
