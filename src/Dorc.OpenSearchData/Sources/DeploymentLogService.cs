using Dorc.ApiModel;
using Dorc.OpenSearchData.Model;
using Dorc.OpenSearchData.Sources.Interfaces;
using log4net;
using OpenSearch.Client;
using System.Collections.Concurrent;

namespace Dorc.OpenSearchData.Sources
{
    public class DeploymentLogService : IDeploymentLogService
    {
        private readonly IOpenSearchClient _openSearchClient;
        private readonly ILog _logger;

        private readonly string _deploymentResultIndex;

        private const int _pageSize = 10000;

        public DeploymentLogService(IOpenSearchClient openSearchClient, ILog logger, string deploymentResultIndex)
        {
            _openSearchClient = openSearchClient;
            _logger = logger;
            _deploymentResultIndex = deploymentResultIndex;
        }

        public void EnrichDeploymentResultsWithLogs(IEnumerable<DeploymentResultApiModel> deploymentResults)
        {
            try
            {
                var requestIds = deploymentResults.Select(deploymentResult => deploymentResult.RequestId).Distinct().ToList();
                var deploymentResultIds = deploymentResults.Select(deploymentResult => deploymentResult.Id).Distinct().ToList();

                var logs = GetLogsFromOpenSearch(requestIds, deploymentResultIds);

                MapLogsToDeploymentResults(deploymentResults, logs);
            }
            catch (Exception e)
            {
                _logger.Error("Request for the deployment result logs to the OpenSearch failed.", e);
                foreach (var deploymentResult in deploymentResults)
                    deploymentResult.Log = "No logs in the OpenSearch or it is unavailable.";
            }
        }

        private IEnumerable<DeployOpenSearchLogModel> GetLogsFromOpenSearch(List<int> requestIds, List<int> deploymentResultIds)
        {
            var logs = new ConcurrentBag<DeployOpenSearchLogModel>();

            Parallel.ForEach(deploymentResultIds, deploymentResultId =>
            {
                var searchResult = _openSearchClient.Search<DeployOpenSearchLogModel>(s => s
                                    .Index(_deploymentResultIndex)
                                    .Query(q => q
                                        .Bool(b => b
                                            .Must(must => must
                                                .Terms(t => t
                                                    .Field(field => field.deployment_result_id)
                                                    .Terms(deploymentResultIds)),
                                                must => must
                                                .Term(t => t
                                                    .Field(field => field.request_id)
                                                    .Value("")))))
                                    .Size(_pageSize));

                if (!searchResult.IsValid)
                {
                    _logger.Error($"OpenSearch query exception: {searchResult.OriginalException?.Message}.{Environment.NewLine}Request information: {searchResult.DebugInformation}");
                    return;
                }

                if (searchResult.Documents != null && searchResult.Documents.Any())
                {
                    foreach (var doc in searchResult.Documents)
                    {
                        logs.Add(doc);
                    }
                }
            });

            return logs;
        }

        private void MapLogsToDeploymentResults(IEnumerable<DeploymentResultApiModel> deploymentResults, IEnumerable<DeployOpenSearchLogModel> logs)
        {
            foreach (var deploymentResult in deploymentResults)
            {
                var deploymentResultLogs = logs.Where(d => d.deployment_result_id == deploymentResult.Id && d.request_id == deploymentResult.RequestId)?.OrderBy(d => d.timestamp);
                if (deploymentResultLogs != null && deploymentResultLogs.Any())
                {
                    deploymentResult.Log = String.Join(Environment.NewLine, deploymentResultLogs.Select(d => $"[{d.timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.ffffff")}] {GetLogLevelString(d)}   {d.message}"));
                }
            }
        }

        private string GetLogLevelString(DeployOpenSearchLogModel logModel)
        {
            return (logModel.level == LogLevel.Error || logModel.level == LogLevel.Warn)
                ? "[" + logModel.level.ToString().ToUpper() + "]"
                : string.Empty;
        }
    }
}
