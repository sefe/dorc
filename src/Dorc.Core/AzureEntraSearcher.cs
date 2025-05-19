using Azure.Identity;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using log4net;
using Microsoft.Graph;
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

            var scopes = new[] { "https://graph.microsoft.com/.default" };

            var clientSecretCredential = new ClientSecretCredential(
                _tenantId, _clientId, _clientSecret);

            _graphClient = new GraphServiceClient(clientSecretCredential, scopes);

            return _graphClient;
        }

        public List<ActiveDirectoryElementApiModel> Search(string objectName)
        {
            var output = new List<ActiveDirectoryElementApiModel>();

            // Restrict the search input to letters only (same as AD implementation)
            if (!Regex.IsMatch(objectName, "^[a-zA-Z-_. ]+$"))
            {
                return output;
            }

            var graphClient = GetGraphClient();

            try
            {
                // Search for users
                var users = graphClient.Users
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Filter =
                            $"startsWith(displayName,'{objectName}') or startsWith(givenName,'{objectName}') or " +
                            $"startsWith(surname,'{objectName}') or startsWith(mail,'{objectName}') or " +
                            $"startsWith(userPrincipalName,'{objectName}')";
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "userPrincipalName", "mail", "accountEnabled" };
                    }).Result;

                foreach (var user in users.Value)
                {
                    if (user.AccountEnabled == true)
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
                        requestConfiguration.QueryParameters.Filter =
                            $"startsWith(displayName,'{objectName}') or startsWith(mailNickname,'{objectName}')";
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "mailNickname", "mail" };
                    }).Result;

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
            catch (Exception ex)
            {
                _log.Error("Error searching Azure Entra ID", ex);
                throw;
            }

            return output;
        }

        public ActiveDirectoryElementApiModel GetUserIdActiveDirectory(string id)
        {
            if (!Regex.IsMatch(id, @"^[a-zA-Z'-_. ]+(\(External\))?$"))
            {
                throw new ArgumentException("Invalid search criteria. Search criteria must be \"^[a-zA-Z-_. ]+(\\(External\\))?$\"!");
            }

            var graphClient = GetGraphClient();

            try
            {
                // Try to find by userPrincipalName first
                var user = graphClient.Users[id]
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "userPrincipalName", "mail", "accountEnabled" };
                    }).Result;

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

                // If not found by userPrincipalName, try search
                var users = graphClient.Users
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Filter =
                            $"startsWith(displayName,'{id}') or startsWith(givenName,'{id}') or " +
                            $"startsWith(surname,'{id}') or startsWith(mail,'{id}') or " +
                            $"startsWith(userPrincipalName,'{id}')";
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

        public List<string> GetSidsForUser(string username)
        {
            var result = new List<string>();
            var graphClient = GetGraphClient();

            try
            {
                // Get the user
                var user = graphClient.Users[username]
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Select = new[] { "id" };
                    }).Result;

                if (user == null)
                {
                    throw new ArgumentException("User not found");
                }

                // Add the user's ID (equivalent to SID in Azure AD)
                result.Add(user.Id);

                // Get all groups the user is a member of (including transitive memberships)
                var memberOf = graphClient.Users[user.Id].MemberOf
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