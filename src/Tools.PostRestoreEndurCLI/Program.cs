using System;
using System.IO;
using System.Runtime.CompilerServices;
using Dorc.Core;
using Dorc.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("Tools.RequestCLI.Tests")]

namespace Tools.PostRestoreEndurCLI
{
    internal class Program
    {
        private static ILogger _logger;

        internal static int Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger("PostRestoreEndurCLI");

            var bolParamsCorrect = false;
            var bolFoldDownAppServers = false;
            var bolOnlyClearSchedules = false;
            var strEnvironment = string.Empty;
            var strBackupFile = string.Empty;
            var strEmailAddress = string.Empty;
            var strSVCAccount = "SVC-MISSING-PARAM";
            foreach (var strArgument in args)
            {
                if (strArgument.ToLower().Contains("/environment:"))
                {
                    bolParamsCorrect = true;
                    strEnvironment = strArgument.Split(':')[1];
                }

                if (strArgument.ToLower().Contains("/folddownappservers:true")) bolFoldDownAppServers = true;
                if (strArgument.ToLower().Contains("/backupfile:")) strBackupFile = strArgument.Split(':')[1];
                if (strArgument.ToLower().Contains("/requestor:")) strEmailAddress = strArgument.Split(':')[1];
                if (strArgument.ToLower().Contains("/svcaccount:")) strSVCAccount = strArgument.Split(':')[1];
                if (strArgument.ToLower().Contains("/folddownappservers:onlyclearschedules"))
                    bolOnlyClearSchedules = true;
            }

            if (bolParamsCorrect)
            {
                try
                {
                    var apiClient = new DorcApiClient(new DorcOAuthClientConfiguration(configuration));
                    var libRefreshEndur = new RefreshEndur(_logger, apiClient);

                    Output("Updating User tables to Dummy Values for: " + strEnvironment);
                    var succeeded = libRefreshEndur.UpdateUserTablesToDummyValues(strEnvironment);

                    succeeded &= libRefreshEndur.UpdateAppServerDetails(strEnvironment, strSVCAccount);
                    succeeded &= libRefreshEndur.UpdateEndurDBVars(strEnvironment);
                    succeeded &= libRefreshEndur.UpdateTPMConfig(strEnvironment);
                    if (bolFoldDownAppServers || bolOnlyClearSchedules)
                        succeeded &= libRefreshEndur.FoldDownRunsites(strEnvironment, bolOnlyClearSchedules);
                    libRefreshEndur.UpdateEnvironmentHistory(strEnvironment, strBackupFile, "Self service refresh", strEmailAddress,
                        "SelfService");
                    succeeded &= libRefreshEndur.UpdateEndurUsers(strEnvironment);

                    if (!succeeded)
                    {
                        Environment.ExitCode = 1;
                        return 1;
                    }

                    return 0;
                }
                catch (InvalidOperationException configEx) when (configEx.Message.Contains("not configured"))
                {
                    Output("Configuration Error: " + configEx.Message);
                    Output("appsettings error");
                    Environment.ExitCode = 1;
                    return 1;
                }
                catch (Exception e)
                {
                    Output("Error: " + e.Message);
                    if (e.InnerException != null)
                    {
                        Output("Inner Error: " + e.InnerException.Message);
                    }
                    Environment.ExitCode = 1;
                    return 1;
                }
            }
            else
            {
                Console.WriteLine("Invalid parameters...");
                Environment.ExitCode = 1;
                return 1;
            }
        }

        private static void Output(string strText)
        {
            var message = DateTime.Now + " - " + strText;
            Console.WriteLine(message);
            _logger?.LogInformation(message);
        }
    }
}