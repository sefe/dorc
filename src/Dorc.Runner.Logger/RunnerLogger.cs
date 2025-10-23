using Dorc.Runner.Logger.Model;
using OpenSearch.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Dorc.Runner.Logger
{
    public class RunnerLogger : IRunnerLogger, IDisposable
    {
        public ILogger FileLogger { get; }
        public IOpenSearchClient OpenSearchClient { get; }


        private int? _requestId;
        private int? _deploymentResultId;
        private string _deploymentResultIndex;
        private string _environment;
        private string _environmentTier;
        private Timer _logTimer;
        private bool _disposedValue;
        private ConcurrentQueue<DeployOpenSearchLogModel> _logMessages;

        public RunnerLogger(ILogger logger, IOpenSearchClient openSearchClient, string deploymentResultIndex, string environment, string environmentTier, int flushEverySec = 10)
        {
            FileLogger = logger;
            OpenSearchClient = openSearchClient;
            _deploymentResultIndex = deploymentResultIndex;
            _environment = environment;
            _environmentTier = environmentTier;
            _logMessages = new ConcurrentQueue<DeployOpenSearchLogModel>();

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
            EnqueueLog(log, OpenSearch.Client.LogLevel.Info);
        }

        public void Information(string message)
        {
            this.FileLogger.LogInformation(message);
            EnqueueLog(message, OpenSearch.Client.LogLevel.Info);
        }
        public void Information(string message, params object[] values)
        {
            this.FileLogger.LogInformation(message, values);
            if (values != null && values.Length > 0)
                EnqueueLog(string.Format(message, values), OpenSearch.Client.LogLevel.Info);
            else
                EnqueueLog(message, OpenSearch.Client.LogLevel.Info);
        }

        public void Verbose(string message)
        {
            this.FileLogger.LogDebug(message);
            EnqueueLog(message, OpenSearch.Client.LogLevel.Info);
        }

        public void Warning(string message)
        {
            this.FileLogger.LogWarning(message);
            EnqueueLog(message, OpenSearch.Client.LogLevel.Warn);
        }

        public void Error(string message)
        {
            this.FileLogger.LogError(message);
            EnqueueLog(message, OpenSearch.Client.LogLevel.Error);
        }
        public void Error(string message, Exception exception)
        {
            this.FileLogger.LogError(message, exception);
            EnqueueLog(message, OpenSearch.Client.LogLevel.Error, exception);
        }
        public void Error(Exception exception, string message, params object[] values)
        {
            this.FileLogger.LogError(exception, message, values);
            if (values != null && values.Length > 0)
                EnqueueLog(string.Format(message, values), OpenSearch.Client.LogLevel.Error, exception);
            else
                EnqueueLog(message, OpenSearch.Client.LogLevel.Error, exception);
        }

        public void Debug(string message)
        {
            this.FileLogger.LogDebug(message);
            EnqueueLog(message, OpenSearch.Client.LogLevel.Debug);
        }

        private void OnLogTimerElapsed(object state)
        {
            FlushLogMessages();
        }

        private void EnqueueLog(string message, OpenSearch.Client.LogLevel type = OpenSearch.Client.LogLevel.Info, Exception exception = null)
        {
            _logMessages.Enqueue(
                new DeployOpenSearchLogModel(
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

            var logList = new List<DeployOpenSearchLogModel>();
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
                    if (!res.IsValid)
                    {
                        this.FileLogger.LogWarning($"Sending \"{String.Join(Environment.NewLine, logList.Select(log => log.message))}\" to the OpenSearch index ({_deploymentResultIndex}) failed." +
                            (res.ServerError != null ? res.ServerError.ToString() : ""));
                    }
                }
                catch (Exception e)
                {
                    this.FileLogger.LogError(e, $"Sending \"{String.Join(Environment.NewLine, logList.Select(log => log.message))}\" to the OpenSearch index ({_deploymentResultIndex}) failed.");
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _logTimer.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
