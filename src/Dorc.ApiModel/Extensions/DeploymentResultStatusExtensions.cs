using System;

namespace Dorc.ApiModel.Extensions
{
    public static class DeploymentResultStatusExtensions
    {
        public static DeploymentResultStatus ParseToDeploymentResultStatus(this string str)
        {
            if (str.Equals(DeploymentResultStatus.StatusNotSet.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return DeploymentResultStatus.StatusNotSet;
            }

            if (str.Equals(DeploymentResultStatus.Disabled.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return DeploymentResultStatus.Disabled;
            }

            if (str.Equals(DeploymentResultStatus.Pending.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return DeploymentResultStatus.Pending;
            }

            if (str.Equals(DeploymentResultStatus.Running.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return DeploymentResultStatus.Running;
            }

            if (str.Equals(DeploymentResultStatus.Complete.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return DeploymentResultStatus.Complete;
            }

            if (str.Equals(DeploymentResultStatus.Warning.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return DeploymentResultStatus.Warning;
            }

            if (str.Equals(DeploymentResultStatus.Cancelled.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return DeploymentResultStatus.Cancelled;
            }

            if (str.Equals(DeploymentResultStatus.Failed.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return DeploymentResultStatus.Failed;
            }

            if (str.Equals(DeploymentResultStatus.WaitingConfirmation.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return DeploymentResultStatus.WaitingConfirmation;
            }

            throw new ArgumentException($"Can't parse '{str}' as DeploymentResultStatus.");
        }
    }
}
