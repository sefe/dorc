﻿using System;
using System.Linq;
using System.Security.Principal;
using Dorc.Core.Exceptions;
using Dorc.Core.Interfaces;
using Dorc.Core.Lamar;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Lamar;

namespace Tools.DeployCopyEnvBuildCLI
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            var registry = new ServiceRegistry();
            registry.IncludeRegistry<PersistentDataRegistry>();
            registry.IncludeRegistry<CoreRegistry>();
            registry.IncludeRegistry<AppRegistry>();

            var container = new Container(registry);
            //Console.WriteLine(container.WhatDidIScan());
            //Console.WriteLine(container.WhatDoIHave());

            var deployLibrary = container.GetInstance<IDeployLibrary>();
            var configValuesPersistentSource = container.GetInstance<IConfigValuesPersistentSource>();
            var intReturnCode = 0;
            
            var whiteList = configValuesPersistentSource.GetConfigValue("DORC_CopyEnvBuildTargetWhitelist");

            if (whiteList == "") return 1;
            
            var arguments = ParseArguments(args);

            if (!whiteList.Contains(arguments.TargetEnv))
            {
                Output(arguments.TargetEnv + " is not a supported target env...");
                return intReturnCode;
            }

            if (!string.IsNullOrEmpty(arguments.Components))
            {
                try
                {
                    var result = deployLibrary.DeployCopyEnvBuildWithComponentNames(arguments.SourceEnv,
                        arguments.TargetEnv, arguments.Project,
                        arguments.Components, new WindowsPrincipal(WindowsIdentity.GetCurrent()));
                    intReturnCode = 0;
                    Output(intReturnCode==0 ? "Request was created!" : "Request wasn't created!");
                }
                catch (WrongComponentsException e)
                {
                    Output(e.Message);
                    intReturnCode = 1;
                }
                catch (Exception e)
                {
                    Output(e.Message);
                    intReturnCode = 1;
                }

            }
            else
            {
                deployLibrary.CopyEnvBuildAllComponents(arguments.SourceEnv, arguments.TargetEnv, arguments.Project, new WindowsPrincipal(WindowsIdentity.GetCurrent()));
            }

            return intReturnCode;
        }

        private static Arguments ParseArguments(string[] args)
        {
            Arguments arguments= new Arguments();
            foreach (var strArgument in args)
                if (strArgument.ToLower().Contains("/sourceenv:"))
                    arguments.SourceEnv = strArgument.Split(':')[1];
                else if (strArgument.ToLower().Contains("/targetenv:"))
                    arguments.TargetEnv = strArgument.Split(':')[1];
                else if (strArgument.ToLower().Contains("/project:"))
                    arguments.Project = strArgument.Split(':')[1];
                else if (strArgument.ToLower().Contains("/components:"))
                    arguments.Components = strArgument.Split(':')[1];

            Output("===================== Copy Env Build =====================");
            Output("=== Source Environment : " + arguments.SourceEnv);
            Output("=== Target Environment : " + arguments.TargetEnv);
            Output("=== Project            : " + arguments.Project);
            if (!string.IsNullOrEmpty(arguments.Components))
            {
                Output("=== Components :         ");
                arguments.Components.Split(';')
                    .ToList()
                    .ForEach(c => Output($"                         {c}"));
            }
            
            Output("==========================================================");
            return arguments;
        }
        private static void Output(string strText)
        {
            Console.WriteLine(DateTime.Now + " - " + strText);
        }
    }

    public class Arguments
    {
        public string SourceEnv { set; get; }
        public string TargetEnv { set; get; }
        public string Project { set; get; }
        public string Components { set; get; }
}
}