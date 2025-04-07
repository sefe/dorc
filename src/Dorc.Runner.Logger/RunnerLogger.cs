using Dorc.PersistData.Dapper;
using Dorc.Runner.Logger.Model;
using OpenSearch.Client;
using Serilog;
using System;

namespace Dorc.Runner.Logger
{
    public class RunnerLogger : IRunnerLogger
    {
        public ILogger Logger { get; }
        public IDapperContext DapperContext { get; }
        public IOpenSearchClient OpenSearchClient { get; }


        private int? _requestId;
        private int? _deploymentResultId;
        private string _deploymentResultIndex;

        public RunnerLogger(ILogger logger, IDapperContext dapperContext, IOpenSearchClient openSearchClient, string deploymentResultIndex)
        {
            Logger = logger;
            DapperContext = dapperContext;
            OpenSearchClient = openSearchClient;
            _deploymentResultIndex = deploymentResultIndex;
        }

        public void SetRequestId(int requestId)
        {
            this._requestId = requestId;
        }

        public void SetDeploymentResultId(int deploymentResultId)
        {
            this._deploymentResultId = deploymentResultId;
        }

        public void AddLogFilePath(int deploymentRequestId, string logFilePath)
        {
            this.DapperContext.AddLogFilePath(this.Logger, deploymentRequestId, logFilePath);
        }

        public void UpdateLog(int deploymentResultId, string log)
        {
            this.DapperContext.UpdateLog(this.Logger, deploymentResultId, log);
            SendDataToOpenSearch(log, LogType.Info);
        }

        public void Information(string message)
        {
            this.Logger.Information(message);
            SendDataToOpenSearch(message, LogType.Info);
        }
        public void Information(string message, params object?[]? values)
        {
            this.Logger.Information(message, values);
            if (values != null && values.Length > 0)
                SendDataToOpenSearch(String.Format(message, values), LogType.Info);
            else
                SendDataToOpenSearch(message, LogType.Info);
        }

        public void Verbose(string message)
        {
            this.Logger.Verbose(message);
            SendDataToOpenSearch(message, LogType.Verbose);
        }

        public void Warning(string message)
        {
            this.Logger.Warning(message);
            SendDataToOpenSearch(message, LogType.Warning);
        }

        public void Error(string message)
        {
            this.Logger.Error(message);
            SendDataToOpenSearch(message, LogType.Error);
        }
        public void Error(string message, Exception exception)
        {
            this.Logger.Error(message, exception);
            SendDataToOpenSearch(message, LogType.Error, exception);
        }
        public void Error(Exception exception, string message, params object?[]? values)
        {
            this.Logger.Error(exception, message, values);
            if (values != null && values.Length > 0)
                SendDataToOpenSearch(String.Format(message, values), LogType.Error, exception);
            else
                SendDataToOpenSearch(message, LogType.Error, exception);
        }

        public void Debug(string message)
        {
            this.Logger.Debug(message);
            SendDataToOpenSearch(message, LogType.Debug);
        }

        private void SendDataToOpenSearch(string message, LogType type = LogType.Info, Exception? exception = null)
        {
            try
            {
                var log = new DeployElasticLog(this._requestId ?? 0, this._deploymentResultId ?? 0, message, type, exception);
                var res = this.OpenSearchClient.IndexAsync<DeployElasticLog>(log, i => i.Index(this._deploymentResultIndex)).Result;
                if (res.Result == Result.Error )
                {
                    this.Logger.Warning($"Sending \"{message}\" to the OpenSearch index ({res.Index}) failed." +
                        res.ServerError != null ? res.ServerError.ToString() : "");
                }
            }
            catch (Exception e)
            {
                this.Logger.Error(e, $"Sending \"{message}\" to the OpenSearch index ({this._deploymentResultIndex}) failed");
            }
        }
    }
}
