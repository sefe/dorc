using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Management;
using CommandLine;
using Dorc.ApiModel.Constants;
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

                var connectionString = config.GetSection("AppSettings")["DOrcConnectionString"];


                Log.Logger = loggerRegistry.InitialiseLogger();

                using (Process process = Process.GetCurrentProcess())
                {
                    string owner = GetProcessOwner(process.Id);
                    Log.Logger.Information("Runner process is started on behalf of the user: " + owner);
                }

                var idx = 0;
                foreach (var s in args)
                {
                    Log.Logger.Information("args[{0}]: {1}", idx++, s);
                }

                Debug.Assert(arguments != null);

                try
                {
                    IScriptGroupProcessor scriptGroupProcessor = new ScriptGroupProcessor(
                        Log.Logger,
                        new ScriptGroupPipeClient(Log.Logger));

                    scriptGroupProcessor.Process(options.PipeName);
                }
                catch (Exception ex)
                {
                    Log.Logger.Error("Deployment error", ex);
                    Console.Error.WriteLine($"Exception caught in Runner: {ex}");

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

            Console.WriteLine(RunnerConstants.StandardStreamEndString);
            Console.Error.WriteLine(RunnerConstants.StandardStreamEndString);
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