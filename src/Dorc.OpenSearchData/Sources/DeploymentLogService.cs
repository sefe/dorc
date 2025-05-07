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

        private const int _pageSize = 10;

        public DeploymentLogService(IOpenSearchClient openSearchClient, ILog logger, string deploymentResultIndex)
        {
            _openSearchClient = openSearchClient;
            _logger = logger;
            _deploymentResultIndex = deploymentResultIndex;
        }

        public void LoadDeploymentResultsLogs(IEnumerable<DeploymentResultApiModel> deploymentResults)
        {
            var requestIds = deploymentResults.Select(deploymentResult => deploymentResult.RequestId).Distinct().ToList();
            var deploymentResultIds = deploymentResults.Select(deploymentResult => deploymentResult.Id).Distinct().ToList();

            var logs = new List<DeployElasticLog>();

            for (int pageNumber = 1; ; pageNumber++)
            {

                var searchResult = _openSearchClient.Search<DeployElasticLog>(s => s
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
                    _logger.Error($"OpenSearch query exception: {searchResult.OriginalException?.Message}.{Environment.NewLine}Query: {searchResult.DebugInformation}");
                    return;
                }

                if (searchResult.Documents != null && searchResult.Documents.Any())
                    logs.AddRange(searchResult.Documents);
                else
                    break;
            }

            foreach (var deploymentResult in deploymentResults)
            {
                var deploymentResultLogs = logs.Where(d => d.deployment_result_id == deploymentResult.Id && d.request_id == deploymentResult.RequestId)?.OrderBy(d => d.timestamp);
                if (deploymentResultLogs != null && deploymentResultLogs.Any())
                {
                    deploymentResult.Log = String.Join(Environment.NewLine, deploymentResultLogs.Select(d => $"[{d.timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.ffffff")}]   {d.message}"));
                }
            }
        }
    }
}
