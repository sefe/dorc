//#define DeploymentRequestTesting

namespace Dorc.ApiModel
{
    public enum DeploymentRequestStatus
    {
        Pending,
        Running,
        Requesting,
        Cancelling,
        Cancelled,
        Restarting,
        Completed,
        Errored,
        Failed,
        Abandoned,
        Superseded,

#if DeploymentRequestTesting
        PendingTesting,
        CancellingTesting,
        RestartingTesting,
        RequestingTesting 
#endif
    }
}