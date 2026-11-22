using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.RequestProcessors;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PowerShell;
using Dorc.Runner;
using Dorc.Runner.Logger;
using Dorc.Runner.Pipes;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Monitor.IntegrationTests.Tests;

[SupportedOSPlatform("windows")]
internal sealed class PipeBackedScriptDispatcher : IScriptDispatcher
{
    private readonly IScriptGroupPipeServer pipeServer;
    private readonly IRequestsPersistentSource requestsPersistentSource;
    private readonly ILoggerFactory loggerFactory;
    private readonly bool useNamedPipeClient;
    private readonly IPowerShellEvidenceCapture evidenceCapture = new PowerShellEvidenceCapture();

    public PipeBackedScriptDispatcher(
        IScriptGroupPipeServer pipeServer,
        IRequestsPersistentSource requestsPersistentSource,
        ILoggerFactory loggerFactory,
        bool useNamedPipeClient)
    {
        this.pipeServer = pipeServer;
        this.requestsPersistentSource = requestsPersistentSource;
        this.loggerFactory = loggerFactory;
        this.useNamedPipeClient = useNamedPipeClient;
    }

    public bool Dispatch(
        string scriptsLocation,
        ScriptApiModel script,
        IDictionary<string, VariableValue> properties,
        int requestId,
        int deploymentResultId,
        bool isProduction,
        string environmentName,
        StringBuilder componentResultLogBuilder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pipeName = $"DOrcMonitor-Test-{requestId}-{deploymentResultId}";
        var currentIdentity = WindowsIdentity.GetCurrent();
        var currentSid = currentIdentity.User!.Value;
        var parts = currentIdentity.Name.Split('\\', 2);
        var readerIdentity = parts.Length == 2
            ? new ScriptGroupReaderIdentity(parts[0], parts[1])
            : new ScriptGroupReaderIdentity(string.Empty, currentIdentity.Name);

        var scriptGroup = new ScriptGroup
        {
            ID = Guid.NewGuid(),
            DeployResultId = deploymentResultId,
            DefinitionRequestId = requestId,
            PowerShellVersionNumber = script.PowerShellVersionNumber,
            ScriptsLocation = scriptsLocation,
            CommonProperties = properties,
            ScriptProperties = new List<ScriptProperties>
            {
                new()
                {
                    ScriptPath = Path.Combine(scriptsLocation, script.Path),
                    Properties = new Dictionary<string, VariableValue>()
                }
            }
        };

        evidenceCapture.Prepare(scriptGroup, script.Revision ?? "rev-1", requestId, 0, readerIdentity.QualifiedAccountName);

        using var pipeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var serverTask = pipeServer.Start(pipeName, scriptGroup, readerIdentity, pipeCts.Token);

            var runnerLogger = Substitute.For<IRunnerLogger>();
            var msLogger = loggerFactory.CreateLogger("InProcessRunner");
            runnerLogger.FileLogger.Returns(msLogger);
            runnerLogger.When(l => l.Information(Arg.Any<string>()))
                .Do(c => componentResultLogBuilder.AppendLine(c.Arg<string>()));
            runnerLogger.When(l => l.Error(Arg.Any<string>()))
                .Do(c => componentResultLogBuilder.AppendLine(c.Arg<string>()));

            IScriptGroupPipeClient pipeClient = useNamedPipeClient
                ? new ScriptGroupPipeClient(runnerLogger, currentSid)
                : new ScriptGroupFileReader(runnerLogger);

            var processor = new ScriptGroupProcessor(runnerLogger, pipeClient);
            var exitCode = processor.Process(pipeName, requestId);

            serverTask.GetAwaiter().GetResult();

            var evidenceValid = evidenceCapture.Ingest(scriptGroup, requestsPersistentSource);
            return exitCode == 0 && evidenceValid;
        }
        finally
        {
            pipeCts.Cancel();
            pipeServer.Expire(pipeName);
        }
    }
}
