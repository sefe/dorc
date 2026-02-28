using Azure.Identity;
using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Authentication;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.CheckMemberGroups;
using System.Text.RegularExpressions;

namespace Dorc.Core
{
    public class AzureEntraSearcher : IActiveDirectorySearcher
    {
        private readonly string _tenantId;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly ILogger _log;
        private GraphServiceClient? _graphClient;

        public AzureEntraSearcher(IConfigurationSettings config, ILogger<AzureEntraSearcher> log)
        {
            _tenantId = config.GetAzureEntraTenantId();
            _clientId = config.GetAzureEntraClientId();
            _clientSecret = config.GetAzureEntraClientSecret();            

            _log = log;
        }

        private GraphServiceClient GetGraphClient()
        {
            if (_graphClient != null)
                return _graphClient;

            if (string.IsNullOrEmpty(_tenantId)) throw new ArgumentNullException("Azure tenantId is not configured");
            if (string.IsNullOrEmpty(_clientId)) throw new ArgumentNullException("Azure clientId is not configured");
            if (string.IsNullOrEmpty(_clientSecret)) throw new ArgumentNullException("Azure clientSecret is not configured");

            try
            {
                var scopes = new[] { "https://graph.microsoft.com/.default" };

                var clientSecretCredential = new ClientSecretCredential(
                    _tenantId,
                    _clientId,
                    _clientSecret,
                    new ClientSecretCredentialOptions
                    {
                        AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,                        
                    });

                var authProvider = new AzureIdentityAuthenticationProvider(
                    clientSecretCredential,
                    scopes: scopes,
                    isCaeEnabled: false
                );

                _graphClient = new GraphServiceClient(authProvider);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error initializing GraphServiceClient.");
                throw new InvalidOperationException("Failed to initialize GraphServiceClient.", ex);
            }

            return _graphClient;
        }

        private static string EscapeODataString(string s) => s?.Replace("'", "''");

        public List<UserElementApiModel> Search(string objectName)
        {
            var output = new List<UserElementApiModel>();

            var graphClient = GetGraphClient();

            objectName = EscapeODataString(objectName);

            try
            {
                // Search for users
                var users = graphClient.Users
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.Headers.Add("ConsistencyLevel", "eventual"); // Required for advanced filtering
                        requestConfiguration.QueryParameters.Count = true; // Enables $count
                        requestConfiguration.QueryParameters.Filter =
                            $"accountEnabled eq true and (" + // only enabled accounts
                            $"startsWith(displayName,'{objectName}') or startsWith(givenName,'{objectName}') or " +
                            $"startsWith(onPremisesSamAccountName,'{objectName}') or " +
                            $"startsWith(surname,'{objectName}') or startsWith(mail,'{objectName}') or " +
                            $"startsWith(userPrincipalName,'{objectName}'))";
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "userPrincipalName", "mail", "accountEnabled", "onPremisesSamAccountName" };
                    }).Result;

                if (users?.Value != null)
                {
                    foreach (var user in users.Value)
                    {
                        output.Add(new UserElementApiModel()
                        {
                            Username = user.UserPrincipalName,
                            DisplayName = user.DisplayName,
                            Pid = user.Id, // In Azure AD, Id is used instead of SID
                            IsGroup = false,
                            Email = user.Mail ?? user.UserPrincipalName
                        });
                    }
                }

                // Search for groups
                var groups = graphClient.Groups
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.Headers.Add("ConsistencyLevel", "eventual"); // Required for advanced filtering
                        requestConfiguration.QueryParameters.Count = true; // Enables $count
                        requestConfiguration.QueryParameters.Filter =
                            $"startsWith(displayName,'{objectName}') or startsWith(mailNickname,'{objectName}') or " +
                            $"startsWith(onPremisesSamAccountName, '{objectName}')";
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "mailNickname", "mail", "onPremisesSamAccountName" };
                    }).Result;

                if (groups?.Value != null)
                {
                    foreach (var group in groups.Value)
                    {
                        output.Add(new UserElementApiModel()
                        {
                            Username = group.MailNickname,
                            DisplayName = group.DisplayName,
                            Pid = group.Id, // In Azure AD, Id is used instead of SID
                            IsGroup = true,
                            Email = group.Mail
                        });
                    }
                }
            }
            catch (ServiceException ex)
            {
                if (ex.ResponseStatusCode == 401 || ex.ResponseStatusCode == 403)
                {
                    _log.LogError(ex, "Authentication/Authorization error when querying Azure Entra ID.");
                    throw new UnauthorizedAccessException("Failed to authenticate or authorize with Azure Entra ID.", ex);
                }

                _log.LogError(ex, "Error searching Azure Entra ID.");
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected error searching Azure Entra ID.");
                throw;
            }

            return output;
        }

        public UserElementApiModel GetUserDataById(string pid)
        {
            if (string.IsNullOrWhiteSpace(pid))
            {
                throw new ArgumentException("The ID cannot be null or empty.");
            }

            var graphClient = GetGraphClient();

            try
            {
                var user = graphClient.Users[pid]
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "userPrincipalName", "mail", "accountEnabled" };
                    }).Result;

                // If a user is found, return the user as ActiveDirectoryElementApiModel
                if (user != null && user.AccountEnabled == true)
                {
                    return new UserElementApiModel()
                    {
                        Username = user.UserPrincipalName,
                        DisplayName = user.DisplayName,
                        Pid = user.Id,
                        IsGroup = false,
                        Email = user.Mail ?? user.UserPrincipalName
                    };
                }
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
            {
                // If the user is not found, swallow the exception and check for a group
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error getting entity from Azure Entra ID");
                throw;
            }

            try
            {
                var group = graphClient.Groups[pid]
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "mailNickname", "mail" };
                    }).Result;

                // If a group is found, return the group as ActiveDirectoryElementApiModel
                if (group != null)
                {
                    return new UserElementApiModel()
                    {
                        Username = group.MailNickname,
                        DisplayName = group.DisplayName,
                        Pid = group.Id,
                        IsGroup = true, // This is a group, not a user
                        Email = group.Mail
                    };
                }
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
            {
                // Group not found, continue to throw ArgumentException
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error getting entity from Azure Entra ID");
                throw;
            }

            // If neither user nor group was found, throw an error
            throw new ArgumentException($"Failed to locate an entity with ID: {pid}");
        }

        public UserElementApiModel GetUserData(string username)
        {
            if (!Regex.IsMatch(username, @"^[a-zA-Z'-_. ]+(\(External\))?$"))
            {
                throw new ArgumentException("Invalid search criteria. Search criteria must be \"^[a-zA-Z-_. ]+(\\(External\\))?$\"!");
            }

            var graphClient = GetGraphClient();
            
            var safeUsername = EscapeODataString(username);

            try
            {
                var users = graphClient.Users
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.Headers.Add("ConsistencyLevel", "eventual"); // Required for advanced filtering
                        requestConfiguration.QueryParameters.Count = true; // Enables $count
                        requestConfiguration.QueryParameters.Filter =
                            $"startsWith(displayName,'{safeUsername}') or startsWith(mail,'{safeUsername}') or " +
                            $"startsWith(onPremisesSamAccountName,'{safeUsername}') or " +
                            $"startsWith(userPrincipalName,'{safeUsername}')";
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "userPrincipalName", "mail", "accountEnabled" };
                    }).Result;

                var activeUser = users.Value.FirstOrDefault(u => u.AccountEnabled == true);
                if (activeUser != null)
                {
                    return new UserElementApiModel()
                    {
                        Username = activeUser.UserPrincipalName,
                        DisplayName = activeUser.DisplayName,
                        Pid = activeUser.Id,
                        IsGroup = false,
                        Email = activeUser.Mail ?? activeUser.UserPrincipalName
                    };
                }
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
            {
                // User not found, continue to throw ArgumentException
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error getting user from Azure Entra ID");
                throw;
            }

            throw new ArgumentException("Failed to locate a valid user account for requested user!");
        }

        public List<string> GetSidsForUser(string userId)
        {
            if (String.IsNullOrEmpty(userId))
            {
                throw new ArgumentException("User ID cannot be null or empty.");
            }

            var result = new List<string> { userId };
            var graphClient = GetGraphClient();

            try
            {
                // Single API call to get ALL group IDs (including transitive)
                var memberGroupsResult = graphClient.Users[userId].GetMemberGroups.PostAsGetMemberGroupsPostResponseAsync(
                    new Microsoft.Graph.Users.Item.GetMemberGroups.GetMemberGroupsPostRequestBody
                    {
                        SecurityEnabledOnly = false,
                    }).GetAwaiter().GetResult();

                if (memberGroupsResult?.Value != null)
                {
                    result.AddRange(memberGroupsResult.Value);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error getting group memberships from Azure Entra ID");
                throw;
            }

            return result;
        }

        public string? GetGroupSidIfUserIsMemberRecursive(string userName, string groupName, string domainName)
        {
            var graphClient = GetGraphClient();

            try
            {
                // Get the user  
                var user = graphClient.Users[userName]
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Select = new[] { "id" };
                    }).Result;

                if (user == null)
                {
                    return string.Empty;
                }

                // Get the group  
                var group = graphClient.Groups
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Filter =
                            $"displayName eq '{groupName}' or mailNickname eq '{groupName}'";
                        requestConfiguration.QueryParameters.Select = new[] { "id" };
                    }).Result;

                var targetGroup = group.Value.FirstOrDefault();
                if (targetGroup == null)
                {
                    return string.Empty;
                }

                // Check if user is a member of the group (including transitive memberships)  
                var requestBody = new CheckMemberGroupsPostRequestBody
                {
                    GroupIds = new List<string> { targetGroup.Id }
                };

                var isMember = graphClient.Users[user.Id].CheckMemberGroups
                    .PostAsCheckMemberGroupsPostResponseAsync(requestBody).Result;

                if (isMember?.Value != null && isMember.Value.Any())
                {
                    return targetGroup.Id;
                }
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.Forbidden)
            {
                _log.LogError(ex, "Insufficient permissions to check group membership");
                throw new System.Configuration.Provider.ProviderException("Insufficient permissions to query Azure Entra ID.", ex);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error checking group membership in Azure Entra ID");
                throw new System.Configuration.Provider.ProviderException("Unable to query Azure Entra ID.", ex);
            }

            return string.Empty;
        }
    }
} 