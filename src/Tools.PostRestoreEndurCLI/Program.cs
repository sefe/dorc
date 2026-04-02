using System;
using System.IO;
using Dorc.Core;
using Dorc.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tools.PostRestoreEndurCLI
{
    internal class Program
    {
        private static ILogger _logger;

        private static void Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
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
                var apiCaller = new ApiCaller(new DorcOAuthClientConfiguration(configuration));
                var libRefreshEndur = new RefreshEndur(_logger, apiCaller);

                Output("Updating User tables to Dummy Values for: " + strEnvironment);
                libRefreshEndur.UpdateUserTablesToDummyValues(strEnvironment);

                libRefreshEndur.UpdateAppServerDetails(strEnvironment, strSVCAccount);
                libRefreshEndur.UpdateEndurDBVars(strEnvironment);
                libRefreshEndur.UpdateTPMConfig(strEnvironment);
                if (bolFoldDownAppServers || bolOnlyClearSchedules)
                    libRefreshEndur.FoldDownRunsites(strEnvironment, bolOnlyClearSchedules);
                libRefreshEndur.UpdateEnvironmentHistory(strEnvironment, strBackupFile, "Self service refresh", strEmailAddress,
                    "SelfService");
                libRefreshEndur.UpdateEndurUsers(strEnvironment);
            }
            else
            {
                Console.WriteLine("Invalid parameters...");
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