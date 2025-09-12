using System.ComponentModel;
using System.Runtime.InteropServices;
using log4net;
using static Dorc.Monitor.RunnerProcess.ProcessSecurityContextBuilder;

namespace Dorc.Monitor.RunnerProcess
{
    internal partial class TerraformRunnerProcessStarter
    {
        private readonly ILog logger;

        public string RunnerExecutableFullName { get; set; } = string.Empty;
        public string ScriptGroupPipeName { get; set; } = string.Empty;
        public string RunnerLogPath { get; set; } = string.Empty;
        public string ScriptPath { get; set; } = string.Empty;
        public string ResultFilePath { get; set; } = string.Empty;

        private TerraformRunnerProcessStarter() { }

        public TerraformRunnerProcessStarter(ILog logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Win32Exception"></exception>
        /// <remarks>
        /// Client code is responsible for disposing of the started instance of <see cref="RunnerProcess"/>.
        /// </remarks>
        public RunnerProcess Start(Interop.Windows.Kernel32.Interop.Kernel32.STARTUPINFO startupInfo, ProcessSecurityContext securityContext)
        {
            #region Verification
            if (string.IsNullOrEmpty(this.RunnerExecutableFullName))
            {
                throw new Exception("Runner executable file full name should be specified.");
            }
            if (!File.Exists(this.RunnerExecutableFullName))
            {
                throw new FileNotFoundException($"Runner executable file is not found. Specified path: '{this.RunnerExecutableFullName}'.");
            }
            if (string.IsNullOrEmpty(this.ScriptPath))
            {
                throw new Exception($"ScriptPath is not specified for Runner process.");
            }
            if (string.IsNullOrEmpty(this.ScriptGroupPipeName))
            {
                throw new Exception($"ScriptGroup pipe name is not specified for Runner process.");
            }
            if (string.IsNullOrEmpty(this.RunnerLogPath))
            {
                throw new Exception($"LogPath is not specified for Runner process.");
            }
            #endregion
                
            var runnerFileInfo = new FileInfo(this.RunnerExecutableFullName);
            string commandLine = runnerFileInfo.Name
                +" -p " + this.ScriptGroupPipeName
                +" -l " + this.RunnerLogPath
                +" -s " + this.ScriptPath
                +" -r " + this.ResultFilePath;
#if DEBUG
            commandLine += " --useFile=true";
#endif
            this.logger.Info($"About to start process {this.RunnerExecutableFullName} with args {commandLine/*ProcessParameters.CommandLine*/}");

            var creationFlags = (uint)Interop.Windows.Advapi32.Interop.Advapi32.ProcessCreationFlags.CREATE_NO_WINDOW |
                    (uint)Interop.Windows.Advapi32.Interop.Advapi32.ProcessCreationFlags.CREATE_UNICODE_ENVIRONMENT;

            var environment = IntPtr.Zero;
            var currentDirectory = runnerFileInfo.DirectoryName!;

            Interop.Windows.Kernel32.Interop.Kernel32.PROCESS_INFORMATION processInformation;

            var result = Interop.Windows.Advapi32.Interop.Advapi32.CreateProcessAsUser(
                securityContext.LocallyLoggedOnUserToken,
                this.RunnerExecutableFullName,
                commandLine,
                ref securityContext.ProcessAttributes,
                ref securityContext.ThreadAttributes,
                true,
                creationFlags,
                environment,
                currentDirectory,
                ref startupInfo,
                out processInformation);

            if (!result)
            {
                var winError = Marshal.GetLastWin32Error();
                var message = $"CreateProcessAsUser failed with win32 error: {winError}";
                this.logger.Error(message);
                throw new Exception(message);
            }

            this.logger.Info("Completed Starting child process");

            var runnerProcess = new RunnerProcess(
                processInformation);

            return runnerProcess;
        }
    }
}
