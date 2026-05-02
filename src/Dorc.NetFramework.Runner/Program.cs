using CommandLine;
using Dorc.ApiModel.Constants;
using Dorc.NetFramework.Runner.Pipes;
using Dorc.Runner.Logger;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Management;
using System.Threading;

namespace Dorc.NetFramework.Runner
{
    internal class Program
    {
        private static Options options;

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

        private static void Main(string[] args)
        {
            IRunnerLogger runnerLogger = null;
            try
            {
                var arguments = Parser.Default.ParseArguments<Options>(args);
                if (arguments.Tag != ParserResultType.Parsed)
                {
                    foreach (var error in arguments.Errors)
                    {
                        Console.Error.WriteLine(error.ToString());
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

                runnerLogger = loggerRegistry.InitializeLogger(options.LogPath, config);

#if LoggingForDebugging
                runnerLogger.Information("Passed arguments:");

                foreach (string arg in args)
                {
                    runnerLogger.Information("\t" + arg);
                }
#endif

                var requestId = int.Parse(options.PipeName.Substring(options.PipeName.IndexOf("-", StringComparison.Ordinal) + 1));
                var dorcPath = loggerRegistry.LogFileName.Replace("c:", @"\\" + System.Environment.GetEnvironmentVariable("COMPUTERNAME"));
                runnerLogger.Information($"Logger Started for pipeline {options.PipeName}: request Id {requestId} formatted path to logs {dorcPath}");

                using (Process process = Process.GetCurrentProcess())
                {
                    string owner = GetProcessOwner(process.Id);
                    runnerLogger.Information($"Runner process is started on behalf of the user: {owner}");
                }

                var idx = 0;
                foreach (var s in args)
                {
                    runnerLogger.Information($"args[{idx++}]: {s}");
                }

                Debug.Assert(arguments != null);

                try
                {
                    IScriptGroupPipeClient scriptGroupReader;

                    if (options.UseFile)
                    {
                        runnerLogger.Debug("Using file instead of pipes");
                        scriptGroupReader = new ScriptGroupFileReader(runnerLogger.FileLogger);
                    }
                    else
                        scriptGroupReader = new ScriptGroupPipeClient(runnerLogger.FileLogger);

                    IScriptGroupProcessor scriptGroupProcessor = new ScriptGroupProcessor(
                        runnerLogger,
                        scriptGroupReader);

                    scriptGroupProcessor.Process(arguments.Value.PipeName, requestId);
                }
                catch (Exception ex)
                {
                    runnerLogger.Error($"Exception occured {ex.Message}");
                    Exit(-1, runnerLogger);
                }
                Exit(0, runnerLogger);
            }
            finally
            {
                FinalizeProgram(runnerLogger);
            }
        }

        static void FinalizeProgram(IRunnerLogger runnerLogger = null)
        {
            Thread.Sleep(10000);
            runnerLogger?.Information(RunnerConstants.StandardStreamEndString);
        }

        static void Exit(int exitCode, IRunnerLogger runnerLogger = null)
        {
            FinalizeProgram(runnerLogger);
            runnerLogger?.Information($"Program Exiting with code {exitCode}");
            Environment.Exit(exitCode);
        }

        public static string GetProcessOwner(int processId)
        {
            string query = "Select * From Win32_Process Where ProcessID = " + processId;
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
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