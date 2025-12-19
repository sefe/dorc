using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.IdentityServer
{
    public class IdentityServerSearcher : IActiveDirectorySearcher
    {
        private readonly IdentityServerClient _client;
        private readonly ILogger _log;

        public IdentityServerSearcher(IConfigurationSettings config, IConfigurationSecretsReader secretsReader, ILogger<IdentityServerSearcher> log, ILoggerFactory loggerFactory)
        {
            _log = log;
            var authority = config.GetOAuthAuthority();
            var clientId = config.GetIdentityServerClientId();
            var clientSecret = secretsReader.GetIdentityServerApiSecret();

            _client = new IdentityServerClient(authority ?? string.Empty, clientId ?? string.Empty, clientSecret ?? string.Empty, loggerFactory.CreateLogger<IdentityServerClient>());
        }

        public List<UserElementApiModel> Search(string objectName)
        {
            try
            {
                var clients = _client.SearchClientsAsync(objectName).GetAwaiter().GetResult();
                return clients.Select(MapToUserElement).ToList();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to search clients in IdentityServer: {ex.Message}");
                throw;
            }
        }

        public UserElementApiModel GetUserData(string name)
        {
            try
            {
                var client = _client.SearchClientsAsync(name, 1, 1).GetAwaiter().GetResult().FirstOrDefault();
                if (client == null)
                {
                    throw new ArgumentException($"Client with name '{name}' not found");
                }

                return MapToUserElement(client);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to get client data from IdentityServer: {ex.Message}");
                throw;
            }
        }

        public UserElementApiModel GetUserDataById(string id)
        {
            try
            {
                var client = _client.GetClientByIdAsync(id).GetAwaiter().GetResult();
                if (client == null)
                {
                    throw new ArgumentException($"Client with ID '{id}' not found");
                }

                return MapToUserElement(client);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to get client data by ID from IdentityServer: {ex.Message}");
                throw;
            }
        }

        public List<string> GetSidsForUser(string username)
        {
            // In IdentityServer context, we don't have SIDs
            // Return the client ID as the only identifier
            return new List<string> { username };
        }

        public string? GetGroupSidIfUserIsMemberRecursive(string userName, string groupName, string domainName)
        {
            // IdentityServer doesn't have groups, so this method is not applicable
            return null;
        }

        private static UserElementApiModel MapToUserElement(ClientInfo client)
        {
            return new UserElementApiModel
            {
                DisplayName = client.ClientName,
                Pid = client.ClientId,
                Username = client.ClientId,
                IsGroup = false, // Clients are not groups
                Email = string.Empty // Clients don't have email addresses
            };
        }
    }
} 