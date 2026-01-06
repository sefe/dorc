using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Configuration;
using Microsoft.Extensions.Configuration;
using RestSharp;
using System;
using System.Linq;
using System.Text.Json;

namespace Tools.DeployCopyEnvBuildCLI
{
    internal class Program
    {
        const string CopyEnvBuildTargetWhitelistPropertyName = "DORC_CopyEnvBuildTargetWhitelist";

        private static int Main(string[] args)
        {

            var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
            var api = new ApiCaller(new DorcOAuthClientConfiguration(config));
            var arguments = ParseArguments(args);
            var whiteList = config[CopyEnvBuildTargetWhitelistPropertyName];
            int intReturnCode = 0;

            if (string.IsNullOrWhiteSpace(whiteList))
            {
                Output("DORC_CopyEnvBuildTargetWhitelist does not have a valid value, should be a semi colon separated list of DOrc environment names");
                return 1;
            }


            if (!whiteList.Contains(arguments.TargetEnv))
            {
                Output(arguments.TargetEnv + " is not a supported target env...");
                return intReturnCode;
            }

            try
            {
                var copyEnvBuildDto = new CopyEnvBuildDto
                {
                    SourceEnv = arguments.SourceEnv,
                    TargetEnv = arguments.TargetEnv,
                    Project = arguments.Project,
                    Components = arguments.Components
                };

                var requestBody = JsonSerializer.Serialize(copyEnvBuildDto);
                var result = api.Call<CopyEnvBuildResponseDto>(Endpoints.CopyEnvBuild, Method.Post, null, requestBody);

                if (!result.IsModelValid || result.Value == null || !result.Value.Success)
                {
                    Output("Error creating requests");
                    Output(result.ErrorMessage ?? result.Value?.Message ?? "Unknown error");
                    return 1;
                }

                Output($"Successfully created {result.Value.RequestIds.Count} request(s):");
                foreach (var id in result.Value.RequestIds)
                {
                    Output($"  - Request ID: {id}");
                }

                intReturnCode = 0;
            }
            catch (Exception e)
            {
                Output(e.Message);
                intReturnCode = 1;
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