using Dorc.ApiModel;
using Tapper;

namespace Dorc.Core.Events
{
    [TranspilationSource]
    public record DeploymentResultEventData(
        int ResultId,
        int RequestId,
        int ComponentId,
        string? Status,
        DateTimeOffset? StartedTime,
        DateTimeOffset? CompletedTime,
        DateTimeOffset Timestamp
    )
    {
        // parametersless constructor for serialization
        public DeploymentResultEventData() 
            : this(
                  0, 
                  0,
                  0,
                  null, 
                  null, 
                  null,
                  DateTimeOffset.UtcNow) { }

        public DeploymentResultEventData(DeploymentResultApiModel apiModel)
            : this(
                  apiModel.Id,
                  apiModel.RequestId,
                  apiModel.ComponentId,
                  apiModel.Status,
                  apiModel.StartedTime,
                  apiModel.CompletedTime,
                  DateTimeOffset.UtcNow)
        { }
    }
}
