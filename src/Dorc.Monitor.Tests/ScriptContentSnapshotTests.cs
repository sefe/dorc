using System.Text;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Configuration;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Monitor.Tests;

[TestClass]
public class ScriptContentSnapshotTests
{
    private const string RecordedBaseline =
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [TestMethod]
    public void MonitorAppSettings_ConfiguresReportAsDefaultScriptContentVerificationMode()
    {
        var appSettingsPath = Path.Join(AppContext.BaseDirectory, "monitor.appsettings.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(appSettingsPath, optional: false)
            .Build();
        var settings = new ConfigurationSettings(configuration);

        Assert.AreEqual(
            "Report",
            configuration["AppSettings:ScriptContentVerification"]);
        Assert.AreEqual(
            ScriptContentVerificationMode.Report,
            settings.GetScriptContentVerificationMode());
    }

    [TestMethod]
    public void ComponentProcessor_DispatchesFrozenSubmittedScriptWithIdAndContentHash()
    {
        var frozenScript = new ScriptApiModel
        {
            Id = 42,
            Name = "Deploy.ps1",
            Path = "Deploy.ps1",
            PowerShellVersionNumber = "7.4",
            ContentHash = RecordedBaseline,
            IsEnabled = true,
            Revision = "rev-42"
        };

        var requests = Substitute.For<IRequestsPersistentSource>();
        requests.UpdateResultStatus(
                Arg.Any<DeploymentResultApiModel>(),
                DeploymentResultStatus.Pending,
                DeploymentResultStatus.Running)
            .Returns(true);
        requests.GetSubmittedScript(77, 12).Returns(frozenScript);

        var configValues = Substitute.For<IConfigValuesPersistentSource>();
        configValues.GetConfigValue("ScriptRoot").Returns(@"\\share\dorc\scripts");

        ScriptApiModel? dispatchedScript = null;
        var scriptDispatcher = Substitute.For<IScriptDispatcher>();
        scriptDispatcher.Dispatch(
                Arg.Any<string>(),
                Arg.Do<ScriptApiModel>(captured => dispatchedScript = captured),
                Arg.Any<IDictionary<string, VariableValue>>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<RequestExecutionIdentityAdoption>(),
                Arg.Any<StringBuilder>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        var processor = new ComponentProcessor(
            scriptDispatcher,
            Substitute.For<ITerraformDispatcher>(),
            requests,
            Substitute.For<IComponentsPersistentSource>(),
            Substitute.For<IDeploymentEventsPublisher>(),
            configValues,
            Substitute.For<ILogger<ComponentProcessor>>());

        var component = new ComponentApiModel
        {
            ComponentId = 12,
            ComponentName = "ApiDeploy",
            ComponentType = ComponentType.PowerShell,
            ScriptPath = "Deploy.ps1",
            IsEnabled = true
        };
        var result = new DeploymentResultApiModel
        {
            Id = 900,
            RequestId = 77,
            Status = DeploymentResultStatus.Pending.ToString()
        };

        processor.DeployComponent(
            component,
            result,
            77,
            isProductionRequest: false,
            environmentId: 3,
            isProductionEnvironment: false,
            environmentName: "DEV",
            executionIdentityReference: null,
            identityAdoption: new RequestExecutionIdentityAdoption(),
            scriptRoot: @"\\share\dorc\scripts",
            commonProperties: new Dictionary<string, VariableValue>(),
            cancellationToken: CancellationToken.None);

        Assert.IsNotNull(dispatchedScript);
        Assert.AreEqual(42, dispatchedScript.Id);
        Assert.AreEqual(RecordedBaseline, dispatchedScript.ContentHash);
    }

    [TestMethod]
    public void LegacySubmittedScriptJsonWithoutContentHash_DeserializesWithNullBaseline()
    {
        const string legacyJson = """
            {"Id":15,"Name":"Legacy.ps1","Path":"Legacy.ps1","IsPathJSON":false,"NonProdOnly":false,"PowerShellVersionNumber":"5.1","IsEnabled":true,"Revision":"rev-legacy"}
            """;

        var deserialized = JsonSerializer.Deserialize<ScriptApiModel>(legacyJson);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(15, deserialized.Id);
        Assert.IsNull(deserialized.ContentHash);
        Assert.AreEqual("rev-legacy", deserialized.Revision);
    }
}
