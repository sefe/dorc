using Dorc.ApiModel;
using Tapper;

namespace Dorc.Core.Events
{
    public enum DeploymentEvents
    {
        RequestStatusChanged,
        ResultStatusChanged,
        NewRequest
    }

    [TranspilationSource]
    public record DeploymentEventData(
        int RequestId,
        string? Status,
        DateTimeOffset? StartedTime,
        DateTimeOffset? CompletedTime,
        string? ProjectName,
        string? EnvironmentName,
        string? BuildNumber,
        string? UserName,
        DateTimeOffset Timestamp
    )
    {
        // Parameterless constructor for serializers that require it
        public DeploymentEventData() : this(
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow
        )
        {
        }

        public DeploymentEventData(DeploymentRequestApiModel drModel)
            : this(
                drModel.Id,
                drModel.Status,
                drModel.StartedTime,
                drModel.CompletedTime,
                drModel.Project,
                drModel.EnvironmentName,
                drModel.BuildNumber,
                drModel.UserName,
                DateTimeOffset.UtcNow
            )
        {
        }

        public DeploymentEventData(RequestStatusDto rModel)
            : this(
                rModel.Id,
                rModel.Status,
                null,
                null,
                null,
                null,
                null,
                null,
                DateTimeOffset.UtcNow
            )
        {
        }
    }
}
