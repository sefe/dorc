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
        WaitingConfirmation,
        Confirmed,
        Paused

#if DeploymentRequestTesting
        PendingTesting,
        CancellingTesting,
        RestartingTesting,
        RequestingTesting 
#endif
    }
}