using Dorc.Kafka.Client.Configuration;
using Microsoft.Extensions.Configuration;

namespace Dorc.Kafka.Client.Tests.Configuration;

/// <summary>
/// regression tests: both checked-in <c>appsettings.json</c> templates
/// (API + Monitor) must expose the <c>Kafka</c> section at the JSON root — the
/// canonical binding path <see cref="KafkaClientOptions.SectionName"/> resolves
/// to. A prior defect ( commit 012f3987) nested the Monitor's block under
/// <c>AppSettings</c> which silently broke <c>IHost.StartAsync</c> on fresh
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
            "Any nesting under AppSettings would silently break IHost.StartAsync() with OptionsValidationException.");
    }

    [TestMethod]
    public void DorcMonitor_Template_Exposes_Kafka_BootstrapServers_At_Root()
    {
        var config = LoadTemplate("dorc-monitor-appsettings.json");

        var bootstrapServers = config[$"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.BootstrapServers)}"];

        Assert.IsNotNull(bootstrapServers,
            "Kafka:BootstrapServers must be present at the JSON root of src/Dorc.Monitor/appsettings.json — " +
            "Kafka must bind from the JSON root, not from under AppSettings.");
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

    [TestMethod]
    public void DorcApi_Template_Exposes_Kafka_SchemaRegistry_Url_At_Root()
    {
        var config = LoadTemplate("dorc-api-appsettings.json");

        var url = config[$"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.SchemaRegistry)}:{nameof(KafkaSchemaRegistryOptions.Url)}"];

        Assert.IsNotNull(url,
            "Kafka:SchemaRegistry:Url must be present at JSON root of src/Dorc.Api/appsettings.json so the installer can write into it. " +
            "The parent key must exist as a template placeholder; AddDorcKafkaAvro throws InvalidOperationException on first serialize if Url is unset at runtime.");
    }

    [TestMethod]
    public void DorcMonitor_Template_Exposes_Kafka_SchemaRegistry_Url_At_Root()
    {
        var config = LoadTemplate("dorc-monitor-appsettings.json");

        var url = config[$"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.SchemaRegistry)}:{nameof(KafkaSchemaRegistryOptions.Url)}"];

        Assert.IsNotNull(url,
            "Kafka:SchemaRegistry:Url must be present at JSON root of src/Dorc.Monitor/appsettings.json so the installer can write into it. " +
            "The placeholder is pre-provisioned for template-shape parity with the API.");
    }

    private static IConfigurationRoot LoadTemplate(string linkedFileName)
    {
        // Path.Join (not Combine) — never silently drops earlier segments
        // even if a future caller ever passes a rooted linkedFileName.
        var path = Path.Join(AppContext.BaseDirectory, "Configuration", linkedFileName);
        Assert.IsTrue(File.Exists(path), $"Linked template not found at {path}. Check the Content/Link declarations in Dorc.Kafka.Client.Tests.csproj.");

        return new ConfigurationBuilder()
            .AddJsonFile(path, optional: false, reloadOnChange: false)
            .Build();
    }
}
