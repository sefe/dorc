using Dorc.Runner.Logger.Model;
using OpenSearch.Client;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Dorc.Runner.Logger
{
    public class RunnerLogger : IRunnerLogger
    {
        public ILogger Logger { get; }
        public IOpenSearchClient OpenSearchClient { get; }


        private int? _requestId;
        private int? _deploymentResultId;
        private string _deploymentResultIndex;
        private string _environment;
        private string _environmentTier;
        private Timer _logTimer;
        private ConcurrentQueue<DeployElasticLog> _logMessages;

        public RunnerLogger(ILogger logger, IOpenSearchClient openSearchClient, string deploymentResultIndex, string environment, string environmentTier, int flushEverySec = 10)
        {
            Logger = logger;
            OpenSearchClient = openSearchClient;
            _deploymentResultIndex = deploymentResultIndex;
            _environment = environment;
            _environmentTier = environmentTier;
            _logMessages = new ConcurrentQueue<DeployElasticLog>();

            _logTimer = new Timer(OnLogTimerElapsed, null, flushEverySec, flushEverySec * 1000);
        }

        public void SetRequestId(int requestId)
        {
            this._requestId = requestId;
        }

        public void SetDeploymentResultId(int deploymentResultId)
        {
            this._deploymentResultId = deploymentResultId;
        }

        public void UpdateLog(int deploymentResultId, string log)
        {
            EnqueueLog(log, LogLevel.Info);
        }

        public void Information(string message)
        {
            this.Logger.Information(message);
            EnqueueLog(message, LogLevel.Info);
        }
        public void Information(string message, params object[] values)
        {
            this.Logger.Information(message, values);
            if (values != null && values.Length > 0)
                EnqueueLog(string.Format(message, values), LogLevel.Info);
            else
                EnqueueLog(message, LogLevel.Info);
        }

        public void Verbose(string message)
        {
            this.Logger.Verbose(message);
            EnqueueLog(message, LogLevel.Info);
        }

        public void Warning(string message)
        {
            this.Logger.Warning(message);
            EnqueueLog(message, LogLevel.Warn);
        }

        public void Error(string message)
        {
            this.Logger.Error(message);
            EnqueueLog(message, LogLevel.Error);
        }
        public void Error(string message, Exception exception)
        {
            this.Logger.Error(message, exception);
            EnqueueLog(message, LogLevel.Error, exception);
        }
        public void Error(Exception exception, string message, params object[] values)
        {
            this.Logger.Error(exception, message, values);
            if (values != null && values.Length > 0)
                EnqueueLog(string.Format(message, values), LogLevel.Error, exception);
            else
                EnqueueLog(message, LogLevel.Error, exception);
        }

        public void Debug(string message)
        {
            this.Logger.Debug(message);
            EnqueueLog(message, LogLevel.Debug);
        }

        private void OnLogTimerElapsed(object state)
        {
            FlushLogMessages();
        }

        private void EnqueueLog(string message, LogLevel type = LogLevel.Info, Exception exception = null)
        {
            _logMessages.Enqueue(
                new DeployElasticLog(
                    this._requestId ?? 0,
                    this._deploymentResultId ?? 0,
                    message,
                    type,
                    exception,
                    _environment,
                    _environmentTier));
        }

        public void FlushLogMessages()
        {
            if (_logMessages.IsEmpty) return;

            var logList = new List<DeployElasticLog>();
            while (_logMessages.TryDequeue(out var log))
            {
                logList.Add(log);
            }

            if (logList.Count > 0)
            {
                try
                {
                    var res = this.OpenSearchClient.Bulk(b => b
                        .Index(_deploymentResultIndex)
                        .IndexMany(logList, (descriptor, document) => descriptor
                            .Document(document)));
                    if (res.IsValid)
                    {
                        this.Logger.Warning($"Sending \"{String.Join(Environment.NewLine, logList.Select(log => log.message))}\" to the OpenSearch index ({_deploymentResultIndex}) failed." +
                            res.ServerError != null ? res.ServerError.ToString() : "");
                    }
                }
                catch (Exception e)
                {
                    this.Logger.Error(e, $"Sending \"{String.Join(Environment.NewLine, logList.Select(log => log.message))}\" to the OpenSearch index ({_deploymentResultIndex}) failed.");
                }
            }
        }
    }
}
