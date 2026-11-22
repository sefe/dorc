using Dorc.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace Dorc.Core.Tests;

[TestClass]
public class ConfigurationSettingsTests
{
    [TestMethod]
    public void RunnerAndRefDataGetters_ReturnConfiguredValues()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppSettings:RefDataApiUrl"] = "https://dorc.example.com",
                ["AppSettings:RunnerLogPath"] = "D:\\Logs\\Runner",
                ["AppSettings:DotNetFrameworkDeploymentRunnerPath"] = "C:\\Runner\\NetFx\\Dorc.NetFramework.Runner.exe",
                ["AppSettings:DotNetCoreDeploymentRunnerPath"] = "C:\\Runner\\NetCore\\Dorc.Runner.exe",
                ["AppSettings:TerraformDeploymentRunnerPath"] = "C:\\Runner\\Terraform\\Dorc.TerraformRunner.exe"
            })
            .Build();

        var settings = new ConfigurationSettings(config);

        Assert.AreEqual("https://dorc.example.com", settings.GetRefDataApiUrl());
        Assert.AreEqual("D:\\Logs\\Runner", settings.GetRunnerLogPath());
        Assert.AreEqual("C:\\Runner\\NetFx\\Dorc.NetFramework.Runner.exe", settings.GetDotNetFrameworkDeploymentRunnerPath());
        Assert.AreEqual("C:\\Runner\\NetCore\\Dorc.Runner.exe", settings.GetDotNetCoreDeploymentRunnerPath());
        Assert.AreEqual("C:\\Runner\\Terraform\\Dorc.TerraformRunner.exe", settings.GetTerraformDeploymentRunnerPath());
    }
}
