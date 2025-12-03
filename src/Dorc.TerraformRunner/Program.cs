using System;
using System.Diagnostics;
using System.Management;
using System.Threading;
using CommandLine;
using Dorc.ApiModel.Constants;
using Dorc.Runner.Logger;
using Dorc.TerraformRunner.Pipes;
using Dorc.TerraformRunner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dorc.TerraformRunner
{
    internal class Program
    {
        private static Options options;
        private static ILogger contextLogger;

        static Program()
        {
            AppDomain.CurrentDomain.UnhandledException +=
                CurrentDomain_UnhandledException;
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            throw new Exception((e.IsTerminating ? "Terminating" : "Non-terminating") +
                                " UnhandledException in Runner: " + e.ExceptionObject + ". Sender: " + sender);
        }

        private static async Task Main(string[] args)
        {
            try
            {
#if LoggingForDebugging
                Console.WriteLine("Passed arguments:");

                foreach (string arg in args)
                {
                    Console.WriteLine("\t" + arg);
                }
#endif

                var arguments = Parser.Default.ParseArguments<Options>(args);

                if (arguments == null)
                    throw new ArgumentNullException(nameof(arguments));

                if (arguments.Tag != ParserResultType.Parsed)
                {
                    foreach (var error in arguments.Errors)
                    {
                        Console.WriteLine(error.ToString());
                    }

                    if (Environment.UserInteractive)
                        Console.ReadKey();

                    Exit(-1);
                }

                options = arguments.Value;
                var loggerRegistry = new LoggerRegistry();
                var config = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json")
                    .AddJsonFile("loggerSettings.json", optional: false)
                    .Build();

                var runnerLogger = loggerRegistry.InitializeLogger(options.LogPath, config);

                contextLogger = runnerLogger.FileLogger;
                var requestId = int.Parse(options.PipeName.Substring(options.PipeName.IndexOf("-", StringComparison.Ordinal) + 1));
                var dorcPath = loggerRegistry.LogFileName.Replace("c:", @"\\" + System.Environment.GetEnvironmentVariable("COMPUTERNAME"));
                contextLogger.LogInformation($"Logger Started for pipeline {options.PipeName}: request Id {requestId} formatted path to logs {dorcPath}");

                using (Process process = Process.GetCurrentProcess())
                {
                    string owner = GetProcessOwner(process.Id);
                    contextLogger.LogInformation("Runner process is started on behalf of the user: {0}", owner);
                }

                var idx = 0;
                foreach (var s in args)
                {
                    contextLogger.LogInformation("args[{0}]: {1}", idx++, s);
                }

                Debug.Assert(arguments != null);

                try
                {
                    IScriptGroupPipeClient scriptGroupReader;

                    if (options.UseFile)
                    {
                        contextLogger.LogDebug("Using file instead of pipes");
                        scriptGroupReader = new ScriptGroupFileReader(contextLogger);
                    }
                    else
                        scriptGroupReader = new ScriptGroupPipeClient(contextLogger);

                    var terraformProcesor = new TerraformProcessor(runnerLogger, scriptGroupReader);

                    switch (options.TerrafromRunnerOperation)
                    {
                        case TerrafromRunnerOperations.CreatePlan:
                            await terraformProcesor.PreparePlanAsync(options.PipeName, requestId, options.ScriptPath, options.PlanFilePath, options.PlanContentFilePath, CancellationToken.None);
                            break;
                        case TerrafromRunnerOperations.ApplyPlan:
                            await terraformProcesor.ExecuteConfirmedPlanAsync(options.PipeName, requestId, options.ScriptPath, options.PlanFilePath, CancellationToken.None);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    contextLogger?.LogError(ex, $"Exception occured {ex.Message}");
                    Exit(-1);
                }
                Exit(0);
            }
            finally
            {
                FinalizeProgram();
            }
        }

        static void FinalizeProgram()
        {
            Thread.Sleep(10000);

            contextLogger?.LogInformation(RunnerConstants.StandardStreamEndString);
        }

        static void Exit(int exitCode)
        {
            FinalizeProgram();
            contextLogger?.LogInformation("Program Exiting with code {0}", exitCode);
            Environment.Exit(exitCode);
        }

        public static string GetProcessOwner(int processId)
        {
            string query = "Select * From Win32_Process Where ProcessID = " + processId;
            using ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
            ManagementObjectCollection processList = searcher.Get();

            foreach (ManagementObject obj in processList)
            {
                string[] argList = new string[] { string.Empty, string.Empty };
                int returnVal = Convert.ToInt32(obj.InvokeMethod("GetOwner", argList));
                if (returnVal == 0)
                {
                    return argList[1] + "\\" + argList[0];
                }
            }

            return "NO OWNER";
        }
    }
}