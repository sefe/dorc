using Dorc.ApiModel;
using Tapper;

namespace Dorc.Core.Events
{
    [TranspilationSource]
    public record DeploymentResultEventData(
        int Id,
        string ComponentName,
        string Status,
        string Log,
        int ComponentId,
        int RequestId,
        DateTimeOffset? StartedTime,
        DateTimeOffset? CompletedTime,
        DateTimeOffset Timestamp
    )
    {
        // parametersless constructor for serialization
        public DeploymentResultEventData() 
            : this(
                  0, 
                  string.Empty, 
                  string.Empty, 
                  string.Empty, 
                  0, 
                  0, 
                  null, 
                  null,
                  DateTimeOffset.UtcNow) { }

        public DeploymentResultEventData(DeploymentResultApiModel apiModel)
            : this(
                  apiModel.Id,
                  apiModel.ComponentName,
                  apiModel.Status,
                  apiModel.Log,
                  apiModel.ComponentId,
                  apiModel.RequestId,
                  apiModel.StartedTime,
                  apiModel.CompletedTime,
                  DateTimeOffset.UtcNow)
        { }
    }
}
