using Dorc.ApiModel;
using Dorc.PersistentData.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.BuildServer
{
    /// <summary>
    /// Factory that returns the appropriate IBuildServerClient based on the project's SourceControlType.
    /// </summary>
    public interface IBuildServerClientFactory
    {
        IBuildServerClient Create(SourceControlType sourceControlType);
    }

    public class BuildServerClientFactory : IBuildServerClientFactory
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IGitHubHostValidator _hostValidator;
        private readonly ISourceHostAllowList _sourceHosts;

        public BuildServerClientFactory(ILoggerFactory loggerFactory, IConfiguration configuration,
            IHttpClientFactory httpClientFactory, IGitHubHostValidator hostValidator,
            ISourceHostAllowList sourceHosts)
        {
            _loggerFactory = loggerFactory;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _hostValidator = hostValidator;
            _sourceHosts = sourceHosts;
        }

        public IBuildServerClient Create(SourceControlType sourceControlType)
        {
            return sourceControlType switch
            {
                SourceControlType.AzureDevOps => new AzureDevOpsBuildServerClient(_loggerFactory, _sourceHosts),
                SourceControlType.GitHub => new GitHubActionsBuildServerClient(
                    _loggerFactory.CreateLogger<GitHubActionsBuildServerClient>(), _configuration, _httpClientFactory, _hostValidator),
                SourceControlType.FileShare => throw new InvalidOperationException(
                    "FileShare projects do not use a build server client. Check SourceControlType before calling Create()."),
                _ => throw new NotSupportedException($"Source control type '{sourceControlType}' is not supported.")
            };
        }
    }
}
