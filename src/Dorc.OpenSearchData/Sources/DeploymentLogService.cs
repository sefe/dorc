using Dorc.ApiModel;
using Dorc.OpenSearchData.Model;
using Dorc.OpenSearchData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using OpenSearchLogLevel = OpenSearch.Client.LogLevel;
using OpenSearch.Client;

namespace Dorc.OpenSearchData.Sources
{
    public class DeploymentLogService : IDeploymentLogService
    {
        private readonly IOpenSearchClient _openSearchClient;
        private readonly ILogger<DeploymentLogService> _logger;

        private readonly string _deploymentResultIndex;

        private const int _pageSize = 5000;

        public DeploymentLogService(IOpenSearchClient openSearchClient, ILogger<DeploymentLogService> logger, string deploymentResultIndex)
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
                _logger.LogError(e, "Request for the deployment result logs to the OpenSearch failed.");
                foreach (var deploymentResult in deploymentResults)
                    deploymentResult.Log = "No logs in the OpenSearch or it is unavailable.";
            }
        }

        private IEnumerable<DeployOpenSearchLogModel> GetLogsFromOpenSearch(List<int> requestIds, List<int> deploymentResultIds)
        {
            var logs = new List<DeployOpenSearchLogModel>();

            for (int pageNumber = 1; ; pageNumber++)
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
                                                .Terms(t => t
                                                    .Field(field => field.request_id)
                                                    .Terms(requestIds)))))
                                    .From((pageNumber - 1) * _pageSize)
                                    .Size(_pageSize));

                if (!searchResult.IsValid)
                {
                    _logger.LogError("OpenSearch query exception: {ExceptionMessage}.{NewLine}Request information: {DebugInformation}", searchResult.OriginalException?.Message, Environment.NewLine, searchResult.DebugInformation);
                    return logs;
                }

                if (searchResult.Documents != null && searchResult.Documents.Any())
                    logs.AddRange(searchResult.Documents);
                else
                    break;
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
            return (logModel.level == OpenSearchLogLevel.Error || logModel.level == OpenSearchLogLevel.Warn)
                ? "[" + logModel.level.ToString().ToUpper() + "]"
                : string.Empty;
        }
    }
}
