//#define LoggingForDebugging

using System.IO.Pipes;
using System.Text.Json;
using Dorc.ApiModel;
using Serilog;

namespace Dorc.Runner.Pipes
{
    internal class ScriptGroupPipeClient : IScriptGroupPipeClient
    {
        private readonly ILogger logger;

        internal ScriptGroupPipeClient(ILogger logger)
        {
            this.logger = logger;
        }

        public ScriptGroup GetScriptGroupProperties(string scriptGroupPipeName)
        {
            this.logger.Information("Pipe name provided for client: '" + scriptGroupPipeName + "'.");
#if LoggingForDebugging
            Console.WriteLine("Pipe name provided for client: '" + scriptGroupPipeName + "'.");
#endif

            try
            {
                using (NamedPipeClientStream pipeClient =
                    new NamedPipeClientStream(".",
                    scriptGroupPipeName,
                    PipeDirection.In))
                {
                    this.logger.Information("Connecting client pipe to server. Pipe name: '" + scriptGroupPipeName + "'.");
#if LoggingForDebugging
                    Console.WriteLine("Connecting client pipe to server. Pipe name: '" + scriptGroupPipeName + "'.");
#endif

                    try
                    {
                        pipeClient.Connect();

                        if (pipeClient.IsConnected)
                        {
                            this.logger.Information("Client pipe is connected.");
#if LoggingForDebugging
                            Console.WriteLine("Client pipe is connected.");
#endif 
                        }
                        else
                        {
                            this.logger.Warning("Client pipe is NOT connected.");
                            Console.WriteLine("Client pipe is NOT connected.");
                        }
                    }
                    catch (Exception e)
                    {
                        this.logger.Error("Exception is thrown while trying to connect to named pipe sever: " + e);
                        Console.Error.WriteLine("Exception is thrown while trying to connect to named pipe sever: " + e);
                        throw;
                    }

                    this.logger.Information("Deserializing received ScriptGroup.");
#if LoggingForDebugging
                    Console.WriteLine("Deserializing received ScriptGroup.");
#endif

                    ScriptGroup scriptGroup = JsonSerializer.Deserialize<ScriptGroup>(pipeClient, JsonSerializerOptions.Default);

                    this.logger.Information("Deserialization of ScriptGroup is completed.");
#if LoggingForDebugging
                    Console.WriteLine("Deserialization of ScriptGroup is completed.");
#endif

                    return scriptGroup;
                }
            }
            catch (Exception ex)
            {
                this.logger.Error("Client pipe has failed. Exception: " + ex);
                Console.Error.WriteLine("Client pipe has failed. Exception: " + ex);
                throw;
            }
        }
    }
}
