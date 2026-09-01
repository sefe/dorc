using Dorc.ApiModel;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.Security
{
    /// <summary>
    /// The environment-keying and fallback accounting shared by every credential source.
    ///
    /// It lives in one place because the interesting property is a negative one: an environment
    /// that names no identity must resolve *exactly* as it did before. Two implementations of
    /// that rule would be two chances to get the fallback subtly wrong, and the failure mode is
    /// silent — a deployment that runs under the wrong account still succeeds.
    /// </summary>
    public abstract class DeploymentCredentialSource : IDeploymentCredentialSource
    {
        private int _bound;
        private int _fallback;

        protected DeploymentCredentialSource(ILogger logger)
        {
            Logger = logger;
        }

        protected ILogger Logger { get; }

        public abstract string Description { get; }

        public (int Bound, int Fallback) ResolutionCounts => (_bound, _fallback);

        public DeploymentCredential? Resolve(DeploymentTier tier) => Resolve(tier, identityReference: null);

        public DeploymentCredential? Resolve(DeploymentTier tier, string? identityReference)
        {
            if (string.IsNullOrWhiteSpace(identityReference))
            {
                Interlocked.Increment(ref _fallback);
                return ResolveTierDefault(tier);
            }

            string validatedIdentityReference;
            try
            {
                validatedIdentityReference =
                    EnvironmentExecutionIdentityReference.Validate(identityReference);
            }
            catch (ArgumentException exception)
            {
                Logger.LogError(
                    exception,
                    "Environment identity reference '{IdentityReference}' is invalid. Refusing to"
                    + " resolve a deployment credential.",
                    identityReference);
                return null;
            }

            var credential = ResolveNamedIdentity(validatedIdentityReference, tier);

            if (credential == null)
            {
                // Deliberately NOT a fallback to the tier default. An environment that names an
                // identity has been bound on purpose; quietly running it under the shared
                // account instead would undo the binding without anyone noticing, and the whole
                // point of binding is that one compromised deployment does not reach the estate.
                Logger.LogError(
                    "Environment identity '{IdentityReference}' could not be resolved from {Source}."
                    + " Refusing to fall back to the {Tier} default - an environment bound to an"
                    + " identity must not silently run under the shared account.",
                    identityReference,
                    Description,
                    tier);

                return null;
            }

            Interlocked.Increment(ref _bound);
            return credential;
        }

        protected abstract DeploymentCredential? ResolveTierDefault(DeploymentTier tier);

        /// <summary>
        /// The credential for a named identity, or null when this source does not hold one.
        /// </summary>
        protected abstract DeploymentCredential? ResolveNamedIdentity(string identityReference, DeploymentTier tier);
    }
}
