﻿using Dorc.ApiModel;
using log4net;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.Monitor.Pipes
{
    internal class ScriptGroupPipeServer : IScriptGroupPipeServer
    {
        private readonly ILog logger;

        private ScriptGroupPipeServer() { }

        public ScriptGroupPipeServer(ILog logger)
        {
            this.logger = logger;
        }

        public Task Start(string pipeName, ScriptGroup scriptGroup, CancellationToken cancellationToken)
        {
            var pipeServerConfiguration = (pipeName, scriptGroup);
            Task scriptGroupPipeTask = Task.Factory.StartNew((object? pipeServerConfiguration) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (pipeServerConfiguration == null
                || !(pipeServerConfiguration is (string, ScriptGroup)))
                {
                    return;
                }

                var namedPipeConfiguration = ((string, ScriptGroup))pipeServerConfiguration;
                string namedPipeName = namedPipeConfiguration.Item1;
                ScriptGroup scriptGroup = namedPipeConfiguration.Item2;

                try
                {
                    using (NamedPipeServerStream pipeServer = new NamedPipeServerStream(
                        namedPipeName,
                        PipeDirection.Out,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous))
                    {
                        logger.Info($"Waiting for pipe client to connect. Pipe name: '{namedPipeName}'.");

                        try
                        {
                            pipeServer.WaitForConnectionAsync(cancellationToken).Wait();
                        }
                        catch (Exception e)
                        {
                            logger.Error($"Exception is thrown while waiting for named pipe client connection. Pipe name: '{namedPipeName}'. Exception: {e}");
                            throw;
                        }

                        logger.Info($"Pipe client has connected. Pipe name: '{namedPipeName}'.");

                        try
                        {
                            var serializeOptions = new JsonSerializerOptions
                            {
                                WriteIndented = true,
                                Converters =
                                {
                                    new VariableValueJsonConverter(),
                                }
                            };
                            JsonSerializer.SerializeAsync(pipeServer, scriptGroup, serializeOptions, cancellationToken).Wait();

                            cancellationToken.ThrowIfCancellationRequested();

                            pipeServer.WaitForPipeDrain();

                            logger.Info($"Pipe client has received serialized ScriptGroup. Pipe name: '{namedPipeName}'.");
                        }
                        catch (Exception e)
                        {
                            logger.Error($"Exception is thrown while sending serialized ScriptGroup. Pipe name: '{namedPipeName}'. Exception: {e}");
                            throw;
                        }
                    }

                    if (Marshal.GetLastWin32Error() != 0)
                    {
                        logger.Error("ScriptGroupPipeServer has failed.");
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                catch (Exception e)
                {
                    logger.Error($"Named pipe has failed. Pipe name: '{namedPipeName}'. Exception: {e}");
                    throw;
                }
            },
            pipeServerConfiguration, cancellationToken);

            return scriptGroupPipeTask;
        }
    }
}
