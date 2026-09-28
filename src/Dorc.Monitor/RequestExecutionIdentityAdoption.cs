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
            if (string.IsNullOrWhiteSpace(identityReference))
            {
                Interlocked.Increment(ref _fallback);
                return credentialSource.Resolve(tier, identityReference);
            }

            var credential = credentialSource.Resolve(tier, identityReference);
            if (credential != null)
            {
                Interlocked.Increment(ref _bound);
            }

            return credential;
        }
    }
}
