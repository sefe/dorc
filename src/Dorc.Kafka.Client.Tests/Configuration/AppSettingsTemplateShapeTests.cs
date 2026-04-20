using Dorc.Kafka.Client.Configuration;
using Microsoft.Extensions.Configuration;

namespace Dorc.Kafka.Client.Tests.Configuration;

/// <summary>
/// S-014 regression tests: both checked-in <c>appsettings.json</c> templates
/// (API + Monitor) must expose the <c>Kafka</c> section at the JSON root — the
/// canonical binding path <see cref="KafkaClientOptions.SectionName"/> resolves
/// to. A prior defect (S-009 commit 012f3987) nested the Monitor's block under
/// <c>AppSettings</c> which silently broke <c>IHost.StartAsync()</c> on fresh
/// MSI installs.
/// </summary>
[TestClass]
public class AppSettingsTemplateShapeTests
{
    [TestMethod]
    public void DorcApi_Template_Exposes_Kafka_BootstrapServers_At_Root()
    {
        var config = LoadTemplate("dorc-api-appsettings.json");

        var bootstrapServers = config[$"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.BootstrapServers)}"];

        Assert.IsNotNull(bootstrapServers,
            "Kafka:BootstrapServers must be present at the JSON root of src/Dorc.Api/appsettings.json. " +
            "Any nesting under AppSettings would silently break IHost.StartAsync() with OptionsValidationException. See SPEC-S-014.");
    }

    [TestMethod]
    public void DorcMonitor_Template_Exposes_Kafka_BootstrapServers_At_Root()
    {
        var config = LoadTemplate("dorc-monitor-appsettings.json");

        var bootstrapServers = config[$"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.BootstrapServers)}"];

        Assert.IsNotNull(bootstrapServers,
            "Kafka:BootstrapServers must be present at the JSON root of src/Dorc.Monitor/appsettings.json. " +
            "Kafka was previously nested under AppSettings (S-009 commit 012f3987); S-014 relocated it to root.");
    }

    [TestMethod]
    public void DorcApi_Template_Does_Not_Nest_Kafka_Under_AppSettings()
    {
        var config = LoadTemplate("dorc-api-appsettings.json");

        var nested = config[$"AppSettings:{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.BootstrapServers)}"];

        Assert.IsNull(nested,
            "AppSettings.Kafka.BootstrapServers must not exist in src/Dorc.Api/appsettings.json — Kafka belongs at JSON root.");
    }

    [TestMethod]
    public void DorcMonitor_Template_Does_Not_Nest_Kafka_Under_AppSettings()
    {
        var config = LoadTemplate("dorc-monitor-appsettings.json");

        var nested = config[$"AppSettings:{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.BootstrapServers)}"];

        Assert.IsNull(nested,
            "AppSettings.Kafka.BootstrapServers must not exist in src/Dorc.Monitor/appsettings.json — Kafka belongs at JSON root.");
    }

    private static IConfigurationRoot LoadTemplate(string linkedFileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Configuration", linkedFileName);
        Assert.IsTrue(File.Exists(path), $"Linked template not found at {path}. Check the Content/Link declarations in Dorc.Kafka.Client.Tests.csproj.");

        return new ConfigurationBuilder()
            .AddJsonFile(path, optional: false, reloadOnChange: false)
            .Build();
    }
}
