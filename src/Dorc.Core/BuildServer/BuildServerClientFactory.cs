using Dorc.ApiModel;
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

        public BuildServerClientFactory(ILoggerFactory loggerFactory, IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _loggerFactory = loggerFactory;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        public IBuildServerClient Create(SourceControlType sourceControlType)
        {
            return sourceControlType switch
            {
                SourceControlType.AzureDevOps => new AzureDevOpsBuildServerClient(_loggerFactory),
                SourceControlType.GitHub => new GitHubActionsBuildServerClient(
                    _loggerFactory.CreateLogger<GitHubActionsBuildServerClient>(), _configuration, _httpClientFactory),
                _ => throw new NotSupportedException($"Source control type '{sourceControlType}' is not supported.")
            };
        }
    }
}
