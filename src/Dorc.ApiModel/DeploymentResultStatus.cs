using System;

namespace Dorc.ApiModel
{
    public class DeploymentResultStatus : IEquatable<DeploymentResultStatus>
    {
        public string Value { get; private set; }

        private DeploymentResultStatus(string value)
        {
            Value = value;
        }

        public static DeploymentResultStatus StatusNotSet { get { return new DeploymentResultStatus("StatusNotSet"); } }
        public static DeploymentResultStatus Disabled { get { return new DeploymentResultStatus("Disabled"); } }
        public static DeploymentResultStatus Pending { get { return new DeploymentResultStatus("Pending"); } }
        public static DeploymentResultStatus Running { get { return new DeploymentResultStatus("Running"); } }
        public static DeploymentResultStatus WaitingConfirmation { get { return new DeploymentResultStatus("WaitingConfirmation"); } }
        public static DeploymentResultStatus Confirmed { get { return new DeploymentResultStatus("Confirmed"); } }

        #region Completed
        public static DeploymentResultStatus Complete { get { return new DeploymentResultStatus("Complete"); } }
        public static DeploymentResultStatus Warning { get { return new DeploymentResultStatus("Warning"); } }
        #endregion
        #region Incompleted
        public static DeploymentResultStatus Cancelled { get { return new DeploymentResultStatus("Cancelled"); } }
        public static DeploymentResultStatus Failed { get { return new DeploymentResultStatus("Failed"); } }
        #endregion

        public override string ToString()
        {
            return Value;
        }

        public bool Equals(DeploymentResultStatus other)
        {
            if (Value == other.Value)
            {
                return true;
            }

            return false;
        }

        public static bool operator ==(DeploymentResultStatus status1, DeploymentResultStatus status2)
        {
            return status1.Equals(status2);
        }

        public static bool operator !=(DeploymentResultStatus status1, DeploymentResultStatus status2)
        {
            return !status1.Equals(status2);
        }
    }
}
