using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Configuration;
using Microsoft.Extensions.Configuration;
using RestSharp;
using System;
using System.Linq;

namespace Tools.RequestCLI
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            int status = 0;
            AppDomain.CurrentDomain.UnhandledException += CatchAllUnhandledExceptions;

            var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();

            Output("=== Execution started ===");

            var cliArgs = new CliArgs(args);
            var api = new ApiCaller(new DorcOAuthClientConfiguration(config));

            Output("============================= Requesting Deploy =============================");
            Output("=== Project            : " + cliArgs.Request.Project);
            Output("=== Target Environment : " + cliArgs.Request.Environment);
            Output("=== Build Text         : " + cliArgs.Request.BuildText);
            Output("=== Build Number       : " + cliArgs.Request.BuildNum);
            Output("=== Build Uri          : " + cliArgs.Request.BuildUrl);
            Output("=== Components         : " + string.Join(" ", cliArgs.Request.Components.ToArray()));
            Output("=== Pinned             : " + cliArgs.Request.Pinned);
            Output("=== Wait               : " + cliArgs.Wait);
            Output("=== Api Root Url       : " + config["DorcApi:BaseUrl"]);
            Output("=============================================================================");
            try
            {
                var result = api.Call<RequestStatusDto>(Endpoints.Request, Method.Post, null, cliArgs.ToString());
                if (!result.IsModelValid || result.Value.Id <= 0)
                {
                    Output("Error creating request");
                    Output(result.ErrorMessage);
                    return 1;
                }

                Output($"Request {result.Value.Id} created");
                if (cliArgs.Wait)
                {
                    var monitor = new RequestMonitor(api);
                    monitor.OnRequestStatusChanged += Output;
                    status = monitor.MonitorRequest(result.Value.Id);
                }
            }
            catch (Exception e)
            {
                DisplayExceptionMessage(e);
                return 1;
            }
            finally
            {
                if (Environment.UserInteractive)
                {
                    Console.ReadKey();
                }
            }
            Output("=== Execution finished ===");
            return status;
        }

        private static void CatchAllUnhandledExceptions(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception) e.ExceptionObject;
            DisplayExceptionMessage(ex);
        }

        private static void DisplayExceptionMessage(Exception ex)
        {
            Output($"Exception was caught, message:\n{ex.Message}");
            if (ex.InnerException != null)
            {
                DisplayExceptionMessage(ex.InnerException);
            }
        }
    
       private static void Output(string strText)
        {
            Console.WriteLine(DateTime.Now + " - " + strText);
        }
    }
}