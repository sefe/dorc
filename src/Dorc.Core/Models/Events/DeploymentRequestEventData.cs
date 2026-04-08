using Dorc.ApiModel;
using Tapper;

namespace Dorc.Core.Events
{
    [TranspilationSource]
    public record DeploymentRequestEventData(
        int RequestId,
        string? Status,
        DateTimeOffset? StartedTime,
        DateTimeOffset? CompletedTime,
        DateTimeOffset Timestamp
    )
    {
        // Parameterless constructor for serializers that require it
        public DeploymentRequestEventData() : this(
            0,
            null,
            null,
            null,
            DateTimeOffset.UtcNow
        )
        {
        }

        public DeploymentRequestEventData(DeploymentRequestApiModel drModel)
            : this(
                drModel.Id,
                drModel.Status,
                drModel.StartedTime,
                drModel.CompletedTime,
                DateTimeOffset.UtcNow
            )
        {
        }
    }
}
