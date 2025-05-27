using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using log4net;

namespace Dorc.Core.IdentityServer
{
    public class IdentityServerSearcher : IActiveDirectorySearcher
    {
        private readonly IdentityServerClient _client;
        private readonly ILog _log;

        public IdentityServerSearcher(IConfigurationSettings config, IConfigurationSecretsReader secretsReader, ILog log)
        {
            _log = log;
            var authority = config.GetOAuthAuthority() ?? throw new ArgumentNullException("OAuthAuthority is not configured");
            var clientId = config.GetIdentityServerClientId() ?? throw new ArgumentNullException("IdentityServerClientId is not configured");
            var clientSecret = secretsReader.GetIdentityServerApiSecret() ?? throw new ArgumentNullException("IdentityServer client secret is not configured");

            _client = new IdentityServerClient(authority, clientId, clientSecret, log);
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
                _log.Error($"Failed to search clients in IdentityServer: {ex.Message}", ex);
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
                _log.Error($"Failed to get client data from IdentityServer: {ex.Message}", ex);
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
                _log.Error($"Failed to get client data by ID from IdentityServer: {ex.Message}", ex);
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