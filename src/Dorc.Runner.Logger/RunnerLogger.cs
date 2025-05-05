using Dorc.PersistData.Dapper;
using Dorc.Runner.Logger.Model;
using Newtonsoft.Json;
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
        private string _environment;
        private string _environmentTier;

        public RunnerLogger(ILogger logger, IDapperContext dapperContext, IOpenSearchClient openSearchClient, string deploymentResultIndex, string environment, string environmentTier)
        {
            Logger = logger;
            DapperContext = dapperContext;
            OpenSearchClient = openSearchClient;
            _deploymentResultIndex = deploymentResultIndex;
            _environment = environment;
            _environmentTier = environmentTier;
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
            SendDataToOpenSearch(log, LogLevel.Info);
        }

        public void UpdateDbLog(int deploymentResultId, string log)
        {
            this.DapperContext.UpdateLog(this.Logger, deploymentResultId, log);
        }

        public void Information(string message)
        {
            this.Logger.Information(message);
            SendDataToOpenSearch(message, LogLevel.Info);
        }
        public void Information(string message, params object[] values)
        {
            this.Logger.Information(message, values);
            if (values != null && values.Length > 0)
                SendDataToOpenSearch(string.Format(message, values), LogLevel.Info);
            else
                SendDataToOpenSearch(message, LogLevel.Info);
        }

        public void Verbose(string message)
        {
            this.Logger.Verbose(message);
            SendDataToOpenSearch(message, LogLevel.Info);
        }

        public void Warning(string message)
        {
            this.Logger.Warning(message);
            SendDataToOpenSearch(message, LogLevel.Warn);
        }

        public void Error(string message)
        {
            this.Logger.Error(message);
            SendDataToOpenSearch(message, LogLevel.Error);
        }
        public void Error(string message, Exception exception)
        {
            this.Logger.Error(message, exception);
            SendDataToOpenSearch(message, LogLevel.Error, exception);
        }
        public void Error(Exception exception, string message, params object[] values)
        {
            this.Logger.Error(exception, message, values);
            if (values != null && values.Length > 0)
                SendDataToOpenSearch(string.Format(message, values), LogLevel.Error, exception);
            else
                SendDataToOpenSearch(message, LogLevel.Error, exception);
        }

        public void Debug(string message)
        {
            this.Logger.Debug(message);
            SendDataToOpenSearch(message, LogLevel.Debug);
        }

        private void SendDataToOpenSearch(string message, LogLevel type = LogLevel.Info, Exception exception = null)
        {
            try
            {
                var log = new DeployElasticLog(this._requestId ?? 0, this._deploymentResultId ?? 0, message, type, exception, _environment, _environmentTier);
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
