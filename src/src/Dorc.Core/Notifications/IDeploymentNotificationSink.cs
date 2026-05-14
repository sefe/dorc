namespace Dorc.Core.Notifications;

public interface IDeploymentNotificationSink
{
    Task NotifyRequestCompletedAsync(Dorc.ApiModel.DeploymentRequestApiModel request, CancellationToken ct = default);
}