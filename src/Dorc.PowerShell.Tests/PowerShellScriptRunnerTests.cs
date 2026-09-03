using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.PowerShell;
using Dorc.Runner.Logger;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.PowerShell.Tests;

[TestClass]
public class PowerShellScriptRunnerTests
{
    [TestMethod]
    public void Run_ScriptWithWriteProgress_DoesNotThrow()
    {
        // Arrange
        var mockLogger = Substitute.For<IRunnerLogger>();
        var mockFileLogger = Substitute.For<ILogger>();
        mockLogger.FileLogger.Returns(mockFileLogger);

        var runner = new PowerShellScriptRunner(mockLogger);

        var scriptFile = Path.Combine(Path.GetTempPath(), $"test_progress_{Guid.NewGuid():N}.ps1");
        try
        {
            // Script that emits progress records — this is what caused the cast exception before the fix
            File.WriteAllText(scriptFile, @"
Write-Progress -Activity 'Testing' -Status 'Step 1' -PercentComplete 50
Write-Output 'Done'
");

            var scriptProperties = new Dictionary<string, VariableValue>();
            var commonProperties = new Dictionary<string, VariableValue>();

            // Act
            int result = runner.Run(string.Empty, scriptFile, scriptProperties, commonProperties);

            // Assert — should succeed (return 0), not crash with InvalidCastException
            Assert.AreEqual(0, result);

            // Verify that the progress message was logged
            mockLogger.Received().Information(Arg.Is<string>(s => s.Contains("[PS]")));
        }
        finally
        {
            if (File.Exists(scriptFile))
                File.Delete(scriptFile);
        }
    }

    [TestMethod]
    public void Run_ScriptWithAllStreams_HandlesAllStreamTypes()
    {
        // Arrange
        var mockLogger = Substitute.For<IRunnerLogger>();
        var mockFileLogger = Substitute.For<ILogger>();
        mockLogger.FileLogger.Returns(mockFileLogger);

        var runner = new PowerShellScriptRunner(mockLogger);

        var scriptFile = Path.Combine(Path.GetTempPath(), $"test_allstreams_{Guid.NewGuid():N}.ps1");
        try
        {
            // Script that exercises all stream types including Progress
            File.WriteAllText(scriptFile, @"
Write-Progress -Activity 'ProgressTest' -Status 'InProgress' -PercentComplete 25
Write-Information 'InfoMessage'
Write-Warning 'WarningMessage'
Write-Verbose -Message 'VerboseMessage' -Verbose
Write-Debug -Message 'DebugMessage' -Debug
Write-Output 'OutputMessage'
");

            var scriptProperties = new Dictionary<string, VariableValue>();
            var commonProperties = new Dictionary<string, VariableValue>();

            // Act — should not throw InvalidCastException on any stream
            int result = runner.Run(string.Empty, scriptFile, scriptProperties, commonProperties);

            // Assert
            Assert.AreEqual(0, result);
        }
        finally
        {
            if (File.Exists(scriptFile))
                File.Delete(scriptFile);
        }
    }
}
