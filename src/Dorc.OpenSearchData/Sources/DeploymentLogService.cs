using Dorc.ApiModel;
using Dorc.OpenSearchData.Model;
using Dorc.OpenSearchData.Sources.Interfaces;
using log4net;
using OpenSearch.Client;

namespace Dorc.OpenSearchData.Sources
{
    public class DeploymentLogService : IDeploymentLogService
    {
        private readonly IOpenSearchClient _openSearchClient;
        private readonly ILog _logger;

        private readonly string _deploymentResultIndex;

        private const int _pageSize = 5000;

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
            var logs = new List<DeployOpenSearchLogModel>();
            const string scrollTimeout = "1m";

            var searchResponse = _openSearchClient.Search<DeployOpenSearchLogModel>(s => s
                .Index(_deploymentResultIndex)
                .Query(q => q
                    .Bool(b => b
                        .Must(must => must
                            .Terms(t => t
                                .Field(field => field.deployment_result_id)
                                .Terms(deploymentResultIds)),
                            must => must
                            .Terms(t => t
                                .Field(field => field.request_id)
                                .Terms(requestIds)))))
                .Size(_pageSize)
                .Scroll(scrollTimeout)
            );

            if (!searchResponse.IsValid)
            {
                _logger.Error($"OpenSearch query exception: {searchResponse.OriginalException?.Message}.{Environment.NewLine}Request information: {searchResponse.DebugInformation}");
                return logs;
            }

            if (searchResponse.Documents != null && searchResponse.Documents.Any())
            {
                logs.AddRange(searchResponse.Documents);
            }

            var scrollId = searchResponse.ScrollId;
            try
            {
                while (!string.IsNullOrEmpty(scrollId))
                {
                    var scrollResponse = _openSearchClient.Scroll<DeployOpenSearchLogModel>(scrollTimeout, scrollId);

                    if (!scrollResponse.IsValid)
                    {
                        _logger.Error($"OpenSearch scroll query exception: {scrollResponse.OriginalException?.Message}.{Environment.NewLine}Request information: {scrollResponse.DebugInformation}");
                        break;
                    }

                    if (scrollResponse.Documents != null && scrollResponse.Documents.Any())
                    {
                        logs.AddRange(scrollResponse.Documents);
                        scrollId = scrollResponse.ScrollId;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(scrollId))
                {
                    _openSearchClient.ClearScroll(c => c.ScrollId(scrollId));
                }
            }

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

        public void EnrichDeploymentResultsWithLimitedLogs(IEnumerable<DeploymentResultApiModel> deploymentResults, int maxLogsPerResult = 3)
        {
            try
            {
                var requestIds = deploymentResults.Select(deploymentResult => deploymentResult.RequestId).Distinct().ToList();
                var deploymentResultIds = deploymentResults.Select(deploymentResult => deploymentResult.Id).Distinct().ToList();

                var logs = GetLimitedLogsFromOpenSearch(requestIds, deploymentResultIds, maxLogsPerResult);

                MapLogsToDeploymentResults(deploymentResults, logs, maxLogsPerResult);
            }
            catch (Exception e)
            {
                _logger.Error("Request for the deployment result logs to the OpenSearch failed.", e);
                foreach (var deploymentResult in deploymentResults)
                    deploymentResult.Log = "No logs in the OpenSearch or it is unavailable.";
            }
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
                _logger.Error($"Request for the deployment result log (RequestId: {requestId}, ResultId: {deploymentResultId}) to the OpenSearch failed.", e);
                return "No logs in the OpenSearch or it is unavailable.";
            }
        }

        /// <summary>
        /// Fetches a limited number of most recent logs from OpenSearch without using Scroll API.
        /// This is optimized for small result sets (e.g., preview/summary logs).
        /// </summary>
        private IEnumerable<DeployOpenSearchLogModel> GetLimitedLogsFromOpenSearch(
            List<int> requestIds,
            List<int> deploymentResultIds,
            int maxLogsPerResult)
        {
            var logs = new List<DeployOpenSearchLogModel>();

            // Calculate the total size: multiply by a factor to account for distribution across results
            // We fetch more than needed and filter in-memory to ensure each result gets its logs
            int fetchSize = Math.Min(maxLogsPerResult * deploymentResultIds.Count * 2, 1000);

            var searchResponse = _openSearchClient.Search<DeployOpenSearchLogModel>(s => s
                .Index(_deploymentResultIndex)
                .Query(q => q
                    .Bool(b => b
                        .Must(
                            must => must.Terms(t => t
                                .Field(field => field.deployment_result_id)
                                .Terms(deploymentResultIds)),
                            must => must.Terms(t => t
                                .Field(field => field.request_id)
                                .Terms(requestIds))
                        )))
                .Size(fetchSize)
                .Sort(sort => sort.Descending(d => d.timestamp)) // Most recent first
            );

            if (!searchResponse.IsValid)
            {
                _logger.Error($"OpenSearch query exception: {searchResponse.OriginalException?.Message}.{Environment.NewLine}Request information: {searchResponse.DebugInformation}");
                return logs;
            }

            if (searchResponse.Documents != null && searchResponse.Documents.Any())
            {
                logs.AddRange(searchResponse.Documents);
            }

            _logger.Debug($"Fetched {logs.Count} limited logs from OpenSearch for {deploymentResultIds.Count} deployment results.");

            return logs;
        }

        private void MapLogsToDeploymentResults(
            IEnumerable<DeploymentResultApiModel> deploymentResults,
            IEnumerable<DeployOpenSearchLogModel> logs,
            int maxLogsPerResult)
        {
            // Pre-group logs by deployment result for efficient lookup
            var logsByResult = logs
                .GroupBy(d => (d.deployment_result_id, d.request_id))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(d => d.timestamp).Take(maxLogsPerResult).ToList()
                );

            foreach (var deploymentResult in deploymentResults)
            {
                var key = (deploymentResult.Id, deploymentResult.RequestId);
                if (logsByResult.TryGetValue(key, out var resultLogs) && resultLogs.Any())
                {
                    var logLines = resultLogs.Select(d =>
                        $"[{d.timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss.ffffff}] {GetLogLevelString(d)}   {d.message}"
                    );

                    deploymentResult.Log = string.Join(Environment.NewLine, logLines);
                }
            }
        }
    }
}
