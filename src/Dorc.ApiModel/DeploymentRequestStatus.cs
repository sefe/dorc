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
        CompletedWithDisabledSteps,
        Errored,
        Failed,
        Abandoned,
        WaitingConfirmation,
        Confirmed

#if DeploymentRequestTesting
        PendingTesting,
        CancellingTesting,
        RestartingTesting,
        RequestingTesting 
#endif
    }
}