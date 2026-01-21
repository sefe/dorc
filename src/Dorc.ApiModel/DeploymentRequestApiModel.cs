        using System;

namespace Dorc.ApiModel
{
    public class DeploymentRequestApiModel
    {
        public int Id { get; set; }
        public string RequestDetails { get; set; }
        public string UserName { get; set; }
        public DateTimeOffset? RequestedTime { get; set; }
        public DateTimeOffset? StartedTime { get; set; }
        public DateTimeOffset? CompletedTime { get; set; }
        public string Status { get; set; }
        public string Log { get; set; }
        public string EnvironmentName { get; set; }
        public string BuildNumber { get; set; }
        public string Components { get; set; }
        public string DropLocation { get; set; }
        public string BuildUri { get; set; }
        public string Project { get; set; }
        public bool IsProd { get; set; }
        public string UncLogPath { get; set; }
        public bool UserEditable { set; get; }
        public int? ParentRequestId { get; set; }
    }
}