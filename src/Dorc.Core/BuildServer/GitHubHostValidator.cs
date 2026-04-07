using Microsoft.Extensions.Configuration;

namespace Dorc.Core.BuildServer
{
    /// <summary>
    /// Validates GitHub API hostnames against an allow-list to prevent SSRF
    /// and token exfiltration via crafted URLs.
    /// </summary>
    public interface IGitHubHostValidator
    {
        /// <summary>
        /// Validates that the given host is an allowed GitHub host.
        /// Throws <see cref="ArgumentException"/> if the host is not allowed.
        /// </summary>
        void ValidateHost(string host);

        /// <summary>
        /// Returns the correct GitHub API base URL for the given server URL.
        /// Validates the host before returning.
        /// </summary>
        string GetApiBase(string serverUrl);
    }

    public class GitHubHostValidator : IGitHubHostValidator
    {
        private static readonly HashSet<string> DefaultAllowedHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "api.github.com",
            "github.com"
        };

        private readonly string[]? _allowedEnterpriseHosts;

        public GitHubHostValidator(IConfiguration configuration)
        {
            _allowedEnterpriseHosts = configuration
                .GetSection("AppSettings:AllowedGitHubEnterpriseHosts")
                .Get<string[]>();
        }

        public void ValidateHost(string host)
        {
            if (DefaultAllowedHosts.Contains(host))
                return;

            if (_allowedEnterpriseHosts != null &&
                _allowedEnterpriseHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
                return;

            throw new ArgumentException(
                $"GitHub API host '{host}' is not in the allowed hosts list. " +
                "Configure 'AllowedGitHubEnterpriseHosts' in AppSettings to allow GitHub Enterprise hosts.");
        }

        public string GetApiBase(string serverUrl)
        {
            var uri = new Uri(serverUrl);
            ValidateHost(uri.Host);

            if (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                return "https://api.github.com";

            // GitHub Enterprise: enforce HTTPS
            return $"https://{uri.Host}/api/v3";
        }
    }
}
