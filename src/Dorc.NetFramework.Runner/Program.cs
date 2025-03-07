﻿using System;
using System.Diagnostics;
using System.Management;
using System.Threading;
using CommandLine;
using Dorc.ApiModel.Constants;
using Dorc.NetFramework.Runner.Pipes;
using Dorc.NetFramework.Runner.Startup;
using Dorc.PersistData.Dapper;
using Microsoft.Extensions.Configuration;
using Serilog;

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
            var loggerRegistry = new LoggerRegistry();
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json").Build();

            var connectionString = config.GetSection("ConnectionStrings")["DOrcConnectionString"];

            var dapperContext = new DapperContext(connectionString);


            Log.Logger = loggerRegistry.InitialiseLogger();

            try
            {
#if LoggingForDebugging
                Log.Logger.Information("Passed arguments:");

                foreach (string arg in args)
                {
                    Log.Logger.Information("\t" + arg);
                }
#endif

                var arguments = Parser.Default.ParseArguments<Options>(args);
                if (arguments.Tag != ParserResultType.Parsed)
                {
                    foreach (var error in arguments.Errors)
                    {
                        Log.Logger.Error(error.ToString());
                    }

                    if (Environment.UserInteractive)
                        Console.ReadKey();

                    Exit(-1);
                }

                options = arguments.Value;

                var contextLogger = Log.Logger.ForContext("PipeName", options.PipeName);
                var requestId = int.Parse(options.PipeName.Substring(options.PipeName.IndexOf("-", StringComparison.Ordinal) + 1));
                var dorcPath = loggerRegistry.logPath.Replace("c:",@"\\"+System.Environment.GetEnvironmentVariable("COMPUTERNAME"));
                contextLogger.Information($"Logger Started for pipeline {options.PipeName}: request Id {requestId} formatted path to logs {dorcPath}");

                string uncLogPath = $"{dorcPath}\\{options.PipeName}.Txt";
                dapperContext.AddLogFilePath(contextLogger,requestId, uncLogPath);

                using (Process process = Process.GetCurrentProcess())
                {
                    string owner = GetProcessOwner(process.Id);
                    contextLogger.Information("Runner process is started on behalf of the user: {0}", owner);
                }

                var idx = 0;
                foreach (var s in args)
                {
                    contextLogger.Information("args[{0}]: {1}", idx++, s);
                }

                Debug.Assert(arguments != null);

                try
                {
                    IScriptGroupPipeClient scriptGroupReader;

                    if (options.UseFile)
                    {
                        contextLogger.Debug("Using file instead of pipes");
                        scriptGroupReader = new ScriptGroupFileReader(contextLogger);
                    }
                    else
                        scriptGroupReader = new ScriptGroupPipeClient(contextLogger);

                    IScriptGroupProcessor scriptGroupProcessor = new ScriptGroupProcessor(
                        contextLogger,
                        dapperContext,
                        scriptGroupReader);

                    scriptGroupProcessor.Process(arguments.Value.PipeName, requestId);
                }
                catch (Exception ex)
                {
                    Log.Logger.Error("Exception occured {0}",ex.Message);
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

            Log.Logger.Information(RunnerConstants.StandardStreamEndString);
        }

        static void Exit(int exitCode)
        {
            FinalizeProgram();
            Log.Logger.Information("Program Exiting with code {0}",exitCode);
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