using System;
using System.Reflection;
using Dorc.Core;
using Dorc.Core.Lamar;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Lamar;
using Microsoft.Extensions.Logging;

namespace Tools.PostRestoreEndurCLI
{
    internal class Program
    {
        private static Container _container;

        private static void Main(string[] args)
        {
            var registry = new ServiceRegistry();
            registry.IncludeRegistry<PersistentDataRegistry>();
            registry.IncludeRegistry<CoreRegistry>();
            registry.IncludeRegistry<CliRegistry>();

            _container = new Container(registry);
            //Console.WriteLine(container.WhatDidIScan());
            //Console.WriteLine(container.WhatDoIHave());

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
                // TODO: Get ILogger from DI container
                var sqlPortsPersistentSource = _container.GetInstance<ISqlPortsPersistentSource>();
                var databasePersistentSource = _container.GetInstance<IDatabasesPersistentSource>();
                var serversPersistentSource = _container.GetInstance<IServersPersistentSource>();
                // TODO: Update RefreshEndur to accept ILogger instead of ILog
                var libRefreshEndur = new RefreshEndur(null, sqlPortsPersistentSource, databasePersistentSource, serversPersistentSource);
                var envHistoryPds = _container.GetInstance<IEnvironmentHistoryPersistentSource>();

                Output("Updating User tables to Dummy Values for: " + strEnvironment);
                libRefreshEndur.UpdateUserTablesToDummyValues(strEnvironment);

                libRefreshEndur.UpdateAppServerDetails(strEnvironment, strSVCAccount);
                libRefreshEndur.UpdateEndurDBVars(strEnvironment);
                libRefreshEndur.UpdateTPMConfig(strEnvironment);
                if (bolFoldDownAppServers || bolOnlyClearSchedules)
                    libRefreshEndur.FoldDownRunsites(strEnvironment, bolOnlyClearSchedules);
                envHistoryPds.UpdateHistory(strEnvironment, strBackupFile, "Self service refresh", strEmailAddress,
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
            Console.WriteLine(DateTime.Now + " - " + strText);
            // TODO: Add proper logging using ILogger from DI container
        }
    }
}