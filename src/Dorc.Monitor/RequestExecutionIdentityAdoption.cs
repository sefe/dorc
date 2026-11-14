using Dorc.ApiModel;
using Dorc.Core.Security;

namespace Dorc.Monitor
{
    public sealed class RequestExecutionIdentityAdoption
    {
        private int _bound;
        private int _fallback;

        public (int Bound, int Fallback) Counts => (_bound, _fallback);

        public DeploymentCredential? Resolve(
            IDeploymentCredentialSource credentialSource,
            DeploymentTier tier,
            string? identityReference)
        {
            var credential = credentialSource.Resolve(tier, identityReference);
            if (credential == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(identityReference))
            {
                Interlocked.Increment(ref _fallback);
            }
            else
            {
                Interlocked.Increment(ref _bound);
            }

            return credential;
        }

        internal static string FormatRefusalMessage(
            string environmentName,
            string? executionIdentityReference,
            string credentialSourceDescription)
        {
            var safeEnvironment = LogText.SingleLine(environmentName);
            var safeSource = LogText.SingleLine(credentialSourceDescription);

            if (string.IsNullOrWhiteSpace(executionIdentityReference))
            {
                return $"Deployment refused: no tier deployment credential could be resolved for environment '{safeEnvironment}' from {safeSource}.";
            }

            var safeIdentity = LogText.SingleLine(executionIdentityReference);
            return $"Deployment refused: execution identity '{safeIdentity}' for environment '{safeEnvironment}' could not be resolved from {safeSource}. Bound environments do not fall back to the shared tier account.";
        }
    }
}
