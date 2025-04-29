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
            this._openSearchClient = openSearchClient;
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
                                                .Field(field => field.DeploymentResultId)
                                                .Terms(deploymentResultIds)),
                                            must => must
                                            .Terms(t => t
                                                .Field(field => field.RequestId)
                                                .Terms(requestIds)))))
                                .Size(10000));

            foreach (var deploymentResult in deploymentResults)
            {
                var deploymentResultLogs = logs.Documents.Where(d => d.DeploymentResultId == deploymentResult.Id && d.RequestId == deploymentResult.RequestId)?.OrderBy(d => d.TimeStamp);
                if (deploymentResultLogs != null && deploymentResultLogs.Any())
                {
                    deploymentResult.Log += Environment.NewLine + "OPENSEARCH LOGS:" + Environment.NewLine;
                    deploymentResult.Log += String.Join(Environment.NewLine, deploymentResultLogs.Select(d => $"[{d.TimeStamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.ffffff")}]   {d.Message}"));
                    deploymentResult.Log += Environment.NewLine + "OPENSEARCH LOGS END" + Environment.NewLine;
                }
            }
        }
    }
}
