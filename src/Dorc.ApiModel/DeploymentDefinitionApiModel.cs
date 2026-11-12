using System;
using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class DeploymentDefinitionApiModel
    {
        public ComponentApiModel Component { get; set; }
        public int ProjectId { get; set; }
        public string ProjectName { get; set; }
        public string Revision { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDeploy { get; set; }
        public string SourceSummary { get; set; }
        public int? ScriptId { get; set; }
    }

    public class GetDeploymentDefinitionsResponse
    {
        public IList<DeploymentDefinitionApiModel> Items { get; set; }
        public int TotalItems { get; set; }
        public int CurrentPage { get; set; }
    }

    public class DeploymentDefinitionHistoryApiModel
    {
        public string Id { get; set; }
        public string EventType { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public string Actor { get; set; }
        public int ProjectId { get; set; }
        public int? ComponentId { get; set; }
        public string ComponentName { get; set; }
        public string Summary { get; set; }
        public int? RequestId { get; set; }
        public int? DeploymentResultId { get; set; }
        public string DefinitionRevision { get; set; }
        public string BeforeJson { get; set; }
        public string AfterJson { get; set; }
        public bool IsLegacy { get; set; }
        public string SourceSummary { get; set; }
    }

    public class GetDeploymentDefinitionHistoryResponse
    {
        public IList<DeploymentDefinitionHistoryApiModel> Items { get; set; }
        public int TotalItems { get; set; }
        public int CurrentPage { get; set; }
    }
}
