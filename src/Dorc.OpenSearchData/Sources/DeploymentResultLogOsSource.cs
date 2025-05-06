using Dorc.ApiModel;
using Dorc.OpenSearchData.Model;
using Dorc.OpenSearchData.Sources.Interfaces;
using OpenSearch.Client;

namespace Dorc.OpenSearchData.Sources
{
    public class DeploymentResultLogOsSource : IDeploymentResultLogOsSource
    {
        private readonly IOpenSearchClient _openSearchClient;

        private readonly string _deploymentResultIndex;

        public DeploymentResultLogOsSource(IOpenSearchClient openSearchClient, string deploymentResultIndex)
        {
            _openSearchClient = openSearchClient;
            _deploymentResultIndex = deploymentResultIndex;
        }

        public void LoadDeploymentResultsLogs(IEnumerable<DeploymentResultApiModel> deploymentResults)
        {
            var requestIds = deploymentResults.Select(deploymentResult => deploymentResult.RequestId).Distinct().ToList();
            var deploymentResultIds = deploymentResults.Select(deploymentResult => deploymentResult.Id).Distinct().ToList();

            var logs = _openSearchClient.Search<DeployElasticLog>(s => s
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
                                .Size(10000));

            foreach (var deploymentResult in deploymentResults)
            {
                var deploymentResultLogs = logs.Documents.Where(d => d.deployment_result_id == deploymentResult.Id && d.request_id == deploymentResult.RequestId)?.OrderBy(d => d.timestamp);
                if (deploymentResultLogs != null && deploymentResultLogs.Any())
                {
                    deploymentResult.Log += String.Join(Environment.NewLine, deploymentResultLogs.Select(d => $"[{d.timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.ffffff")}]   {d.message}"));
                }
            }
        }
    }
}
