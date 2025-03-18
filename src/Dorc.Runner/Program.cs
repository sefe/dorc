using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Management;
using CommandLine;
using Dorc.ApiModel.Constants;
using Dorc.PersistData.Dapper;
using Dorc.Runner.Pipes;
using Dorc.Runner.Startup;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Dorc.Runner
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
            Log.Logger?.Error(e.ExceptionObject as Exception, "UnhandledException in Runner");

            throw new Exception((e.IsTerminating ? "Terminating" : "Non-terminating") +
                                " UnhandledException in Runner: " + e.ExceptionObject + ". Sender: " + sender);
        }

        private static void Main(string[] args)
        {
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
                    .AddJsonFile("appsettings.json").Build();

                var connectionString = config.GetSection("ConnectionStrings")["DOrcConnectionString"];

                var dapperContext = new DapperContext(connectionString);

                Log.Logger = loggerRegistry.InitialiseLogger(options.PipeName);

                var requestId = int.Parse(options.PipeName.Substring(options.PipeName.IndexOf("-", StringComparison.Ordinal) + 1));
                var uncDorcPath = loggerRegistry.LogFileName.Replace("c:", @"\\" + Environment.GetEnvironmentVariable("COMPUTERNAME"));
                Log.Logger.Information("Runner Started for pipename {0}: formatted path to logs {1}", options.PipeName, loggerRegistry.LogFileName);

                dapperContext.AddLogFilePath(Log.Logger, requestId, uncDorcPath);

                using (Process process = Process.GetCurrentProcess())
                {
                    string owner = GetProcessOwner(process.Id);
                    Log.Logger.Information("Runner process is started on behalf of the user: {0}", owner);
                }
                
                Log.Logger.Information("Arguments: {args}", string.Join(", ", args));
  
                try
                {
                    IScriptGroupPipeClient scriptGroupReader;
                    if (options.UseFile)
                    {
                        Log.Logger.Debug("Using file instead of pipes");
                        scriptGroupReader = new ScriptGroupFileReader(Log.Logger);
                    }
                    else
                        scriptGroupReader = new ScriptGroupPipeClient(Log.Logger);

                    IScriptGroupProcessor scriptGroupProcessor = new ScriptGroupProcessor(
                        Log.Logger,
                        scriptGroupReader, dapperContext);

                    var result = scriptGroupProcessor.Process(options.PipeName, requestId);
                    Exit(result);
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex, "Deployment error");

                    Exit(-1);
                    throw;
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
            Environment.Exit(exitCode);
        }

        #region Debug
        [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
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
                    // return DOMAIN\user
                    return argList[1] + "\\" + argList[0];
                }
            }

            return "NO OWNER";
        }
        #endregion
    }
}