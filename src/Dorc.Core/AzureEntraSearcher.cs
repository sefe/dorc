using Azure.Identity;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using log4net;
using Microsoft.Graph;
using Microsoft.Graph.Authentication;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.CheckMemberGroups;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Dorc.Core
{
    [SupportedOSPlatform("windows")]
    public class AzureEntraSearcher : IActiveDirectorySearcher
    {
        private readonly string _tenantId;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly ILog _log;
        private GraphServiceClient? _graphClient;

        public AzureEntraSearcher(string tenantId, string clientId, string clientSecret, ILog log)
        {
            _tenantId = tenantId;
            _clientId = clientId;
            _clientSecret = clientSecret;
            _log = log;
        }

        private GraphServiceClient GetGraphClient()
        {
            if (_graphClient != null)
                return _graphClient;

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
                _log.Error("Error initializing GraphServiceClient.", ex);
                throw new InvalidOperationException("Failed to initialize GraphServiceClient.", ex);
            }

            return _graphClient;
        }

        public List<ActiveDirectoryElementApiModel> Search(string objectName)
        {
            var output = new List<ActiveDirectoryElementApiModel>();

            var graphClient = GetGraphClient();

            try
            {
                // Search for users
                var users = graphClient.Users
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.Headers.Add("ConsistencyLevel", "eventual"); // Required for advanced filtering
                        requestConfiguration.QueryParameters.Count = true; // Enables $count
                        requestConfiguration.QueryParameters.Filter =
                            $"accountEnabled eq true and " + // only enabled accounts
                            $"startsWith(displayName,'{objectName}') or startsWith(givenName,'{objectName}') or " +
                            $"startsWith(onPremisesSamAccountName,'{objectName}') or " +
                            $"startsWith(surname,'{objectName}') or startsWith(mail,'{objectName}') or " +
                            $"startsWith(userPrincipalName,'{objectName}')";
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "userPrincipalName", "mail", "accountEnabled", "onPremisesSamAccountName" };
                    }).Result;

                if (users?.Value != null)
                {
                    foreach (var user in users.Value)
                    {
                        output.Add(new ActiveDirectoryElementApiModel()
                        {
                            Username = user.UserPrincipalName,
                            DisplayName = user.DisplayName,
                            Sid = user.Id, // In Azure AD, Id is used instead of SID
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
                        output.Add(new ActiveDirectoryElementApiModel()
                        {
                            Username = group.MailNickname,
                            DisplayName = group.DisplayName,
                            Sid = group.Id, // In Azure AD, Id is used instead of SID
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
                    _log.Error("Authentication/Authorization error when querying Azure Entra ID.", ex);
                    throw new UnauthorizedAccessException("Failed to authenticate or authorize with Azure Entra ID.", ex);
                }

                _log.Error("Error searching Azure Entra ID.", ex);
                throw;
            }
            catch (Exception ex)
            {
                _log.Error("Unexpected error searching Azure Entra ID.", ex);
                throw;
            }

            return output;
        }

        public ActiveDirectoryElementApiModel GetEntityById(string pid)
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
                    return new ActiveDirectoryElementApiModel()
                    {
                        Username = user.UserPrincipalName,
                        DisplayName = user.DisplayName,
                        Sid = user.Id,
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
                _log.Error("Error getting entity from Azure Entra ID", ex);
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
                    return new ActiveDirectoryElementApiModel()
                    {
                        Username = group.MailNickname,
                        DisplayName = group.DisplayName,
                        Sid = group.Id,
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
                _log.Error("Error getting entity from Azure Entra ID", ex);
                throw;
            }

            // If neither user nor group was found, throw an error
            throw new ArgumentException($"Failed to locate an entity with ID: {pid}");
        }

        public ActiveDirectoryElementApiModel GetUserData(string username)
        {
            if (!Regex.IsMatch(username, @"^[a-zA-Z'-_. ]+(\(External\))?$"))
            {
                throw new ArgumentException("Invalid search criteria. Search criteria must be \"^[a-zA-Z-_. ]+(\\(External\\))?$\"!");
            }

            var graphClient = GetGraphClient();

            try
            {
                var users = graphClient.Users
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.Headers.Add("ConsistencyLevel", "eventual"); // Required for advanced filtering
                        requestConfiguration.QueryParameters.Count = true; // Enables $count
                        requestConfiguration.QueryParameters.Filter =
                            $"startsWith(displayName,'{username}') or startsWith(mail,'{username}') or " +
                            $"startsWith(onPremisesSamAccountName,'{username}') or " +
                            $"startsWith(userPrincipalName,'{username}')";
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "userPrincipalName", "mail", "accountEnabled" };
                    }).Result;

                var activeUser = users.Value.FirstOrDefault(u => u.AccountEnabled == true);
                if (activeUser != null)
                {
                    return new ActiveDirectoryElementApiModel()
                    {
                        Username = activeUser.UserPrincipalName,
                        DisplayName = activeUser.DisplayName,
                        Sid = activeUser.Id,
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
                _log.Error("Error getting user from Azure Entra ID", ex);
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

            var result = new List<string>();
            var graphClient = GetGraphClient();

            try
            {
                result.Add(userId);

                // Get all groups the user is a member of (including transitive memberships)
                var memberOf = graphClient.Users[userId].MemberOf
                    .GetAsync().Result;

                var pageIterator = PageIterator<DirectoryObject, DirectoryObjectCollectionResponse>
                    .CreatePageIterator(
                        graphClient,
                        memberOf,
                        (directoryObject) =>
                        {
                            if (directoryObject is Microsoft.Graph.Models.Group group)
                            {
                                result.Add(group.Id);
                            }
                            return true;
                        });

                // Iterate through all pages
                pageIterator.IterateAsync().Wait();
            }
            catch (Exception ex)
            {
                _log.Error("Error getting group memberships from Azure Entra ID", ex);
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
                _log.Error("Insufficient permissions to check group membership", ex);
                throw new System.Configuration.Provider.ProviderException("Insufficient permissions to query Azure Entra ID.", ex);
            }
            catch (Exception ex)
            {
                _log.Error("Error checking group membership in Azure Entra ID", ex);
                throw new System.Configuration.Provider.ProviderException("Unable to query Azure Entra ID.", ex);
            }

            return string.Empty;
        }
    }
} 