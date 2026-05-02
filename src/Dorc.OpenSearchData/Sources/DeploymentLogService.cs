using Dorc.ApiModel;
using Dorc.OpenSearchData.Model;
using Dorc.OpenSearchData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using OpenSearch.Client;
using System.Collections.Concurrent;

namespace Dorc.OpenSearchData.Sources
{
    public class DeploymentLogService : IDeploymentLogService
    {
        private readonly IOpenSearchClient _openSearchClient;
        private readonly ILogger _logger;

        private readonly string _deploymentResultIndex;

        private const int _pageSize = 10000;

        public DeploymentLogService(IOpenSearchClient openSearchClient, ILogger<DeploymentLogService> logger, string deploymentResultIndex)
        {
            _openSearchClient = openSearchClient;
            _logger = logger;
            _deploymentResultIndex = deploymentResultIndex;
        }

        public void EnrichDeploymentResultsWithLogs(IEnumerable<DeploymentResultApiModel> deploymentResults, int? maxLogsPerResult = null)
        {
            try
            {
                var requestIds = deploymentResults.Select(deploymentResult => deploymentResult.RequestId).Distinct().ToList();
                var deploymentResultIds = deploymentResults.Select(deploymentResult => deploymentResult.Id).Distinct().ToList();

                var logs = GetLogsFromOpenSearch(requestIds, deploymentResultIds, maxLogsPerResult);

                MapLogsToDeploymentResults(deploymentResults, logs);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Request for the deployment result logs to the OpenSearch failed.");
                foreach (var deploymentResult in deploymentResults)
                    deploymentResult.Log = "No logs in the OpenSearch or it is unavailable.";
            }
        }

        private IEnumerable<DeployOpenSearchLogModel> GetLogsFromOpenSearch(
            List<int> requestIds, 
            List<int> deploymentResultIds,
            int? maxLogsPerResult = null)
        {
            var logs = new ConcurrentBag<DeployOpenSearchLogModel>();

            Parallel.ForEach(deploymentResultIds, deploymentResultId =>
            {
                var searchResult = _openSearchClient.Search<DeployOpenSearchLogModel>(s => s
                                    .Index(_deploymentResultIndex)
                                    .Query(q => q
                                        .Bool(b => b
                                            .Must(must => must
                                                .Term(t => t
                                                    .Field(field => field.deployment_result_id)
                                                    .Value(deploymentResultId)),
                                                must => must
                                                .Terms(t => t
                                                    .Field(field => field.request_id)
                                                    .Terms(requestIds)))))
                                    .Sort(sort => sort.Ascending(d => d.timestamp))
                                    .Size(maxLogsPerResult ?? _pageSize));

                if (!searchResult.IsValid)
                {
                    _logger.LogError($"OpenSearch query exception: {searchResult.OriginalException?.Message}.{Environment.NewLine}Request information: {searchResult.DebugInformation}");
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
                    deploymentResult.Log = String.Join(Environment.NewLine, deploymentResultLogs.Select(d => $"[{d.timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.ffffff")}] {GetLogLevelString(d)} {d.message}"));
                }
            }
        }

        private string GetLogLevelString(DeployOpenSearchLogModel logModel)
        {
            return "[" + logModel.level.ToString().ToUpper() + "]";
        }

        public string GetLogsForSingleResult(int requestId, int deploymentResultId)
        {
            try
            {
                var logs = GetLogsFromOpenSearch(new List<int> { requestId }, new List<int> { deploymentResultId });

                var orderedLogs = logs
                    .Where(d => d.deployment_result_id == deploymentResultId && d.request_id == requestId)
                    .OrderBy(d => d.timestamp)
                    .ToList();

                if (!orderedLogs.Any())
                {
                    return string.Empty;
                }

                return string.Join(Environment.NewLine,
                    orderedLogs.Select(d =>
                        $"[{d.timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss.ffffff}] {GetLogLevelString(d)}   {d.message}"));
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Request for the deployment result log (RequestId: {requestId}, ResultId: {deploymentResultId}) to the OpenSearch failed.");
                return "No logs in the OpenSearch or it is unavailable.";
            }
        }
    }
}
