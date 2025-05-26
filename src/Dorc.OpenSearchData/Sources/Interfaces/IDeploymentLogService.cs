﻿using Dorc.ApiModel;

namespace Dorc.OpenSearchData.Sources.Interfaces
{
    public interface IDeploymentLogService
    {
        void EnrichDeploymentResultsWithLogs(IEnumerable<DeploymentResultApiModel> deploymentResults);
    }
}
