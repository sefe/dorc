using Azure.Identity;
using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Authentication;
using Microsoft.Graph.Users.Item.CheckMemberGroups;
using Microsoft.Kiota.Abstractions;
using System.Text.RegularExpressions;

namespace Dorc.Core
{
    public class AzureEntraSearcher : IActiveDirectorySearcher
    {
        // Matches well-formed Windows/AD SIDs (S-1-5-..., S-1-12-...). Used to decide
        // whether to fall back to onPremisesSecurityIdentifier filter queries on direct-lookup 404.
        private static readonly Regex AdSidShape = new("^S-1-(5|12)-\\d+(-\\d+)*$", RegexOptions.Compiled);

        private readonly string _tenantId;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly ILogger _log;
        private readonly Func<GraphServiceClient>? _graphClientFactory;
        private GraphServiceClient? _graphClient;

        public AzureEntraSearcher(IConfigurationSettings config, ILogger<AzureEntraSearcher> log)
        {
            _tenantId = config.GetAzureEntraTenantId();
            _clientId = config.GetAzureEntraClientId();
            _clientSecret = config.GetAzureEntraClientSecret();

            _log = log;
        }

        // Test-seam ctor: injects a pre-built GraphServiceClient so tests can drive a fake.
        // Per SPEC-S-001 §3.1 — the only way to satisfy HLPS SC-9 (integration-level Graph-fake tests).
        internal AzureEntraSearcher(Func<GraphServiceClient> graphClientFactory, ILogger<AzureEntraSearcher> log)
        {
            _tenantId = string.Empty;
            _clientId = string.Empty;
            _clientSecret = string.Empty;
            _graphClientFactory = graphClientFactory;
            _log = log;
        }

        private GraphServiceClient GetGraphClient()
        {
            if (_graphClient != null)
                return _graphClient;

            if (_graphClientFactory != null)
            {
                _graphClient = _graphClientFactory();
                return _graphClient;
            }

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

        private static string EscapeODataString(string s) => s?.Replace("'", "''") ?? string.Empty;

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
                        requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
                        requestConfiguration.QueryParameters.Count = true;
                        requestConfiguration.QueryParameters.Filter =
                            $"accountEnabled eq true and (" +
                            $"startsWith(displayName,'{objectName}') or startsWith(givenName,'{objectName}') or " +
                            $"startsWith(onPremisesSamAccountName,'{objectName}') or " +
                            $"startsWith(surname,'{objectName}') or startsWith(mail,'{objectName}') or " +
                            $"startsWith(userPrincipalName,'{objectName}'))";
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "userPrincipalName", "mail", "accountEnabled", "onPremisesSamAccountName", "onPremisesSecurityIdentifier" };
                    }).GetAwaiter().GetResult();

                if (users?.Value != null)
                {
                    foreach (var user in users.Value)
                    {
                        output.Add(new UserElementApiModel
                        {
                            Username = user.UserPrincipalName,
                            DisplayName = user.DisplayName,
                            Pid = user.Id,
                            Sid = user.OnPremisesSecurityIdentifier,
                            IsGroup = false,
                            Email = user.Mail ?? user.UserPrincipalName
                        });
                    }
                }

                // Search for groups
                var groups = graphClient.Groups
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
                        requestConfiguration.QueryParameters.Count = true;
                        requestConfiguration.QueryParameters.Filter =
                            $"startsWith(displayName,'{objectName}') or startsWith(mailNickname,'{objectName}') or " +
                            $"startsWith(onPremisesSamAccountName, '{objectName}')";
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "mailNickname", "mail", "onPremisesSamAccountName", "onPremisesSecurityIdentifier" };
                    }).GetAwaiter().GetResult();

                if (groups?.Value != null)
                {
                    foreach (var group in groups.Value)
                    {
                        output.Add(new UserElementApiModel
                        {
                            Username = group.MailNickname,
                            DisplayName = group.DisplayName,
                            Pid = group.Id,
                            Sid = group.OnPremisesSecurityIdentifier,
                            IsGroup = true,
                            Email = group.Mail
                        });
                    }
                }
            }
            catch (ApiException ex)
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
            var isSidShaped = AdSidShape.IsMatch(pid);

            // Direct user lookup
            try
            {
                var user = graphClient.Users[pid]
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "userPrincipalName", "mail", "accountEnabled", "onPremisesSecurityIdentifier" };
                    }).GetAwaiter().GetResult();

                if (user != null && user.AccountEnabled == true)
                {
                    return new UserElementApiModel
                    {
                        Username = user.UserPrincipalName,
                        DisplayName = user.DisplayName,
                        Pid = user.Id,
                        Sid = user.OnPremisesSecurityIdentifier,
                        IsGroup = false,
                        Email = user.Mail ?? user.UserPrincipalName
                    };
                }
            }
            catch (ApiException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
            {
                // Fall through to SID-shape user fallback / group lookup
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error getting entity from Azure Entra ID");
                throw;
            }

            // P-4 user fallback: SID-shaped input — try onPremisesSecurityIdentifier filter
            if (isSidShaped)
            {
                var hit = FindUserByOnPremisesSid(graphClient, pid);
                if (hit != null) return hit;
            }

            // Direct group lookup
            try
            {
                var group = graphClient.Groups[pid]
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "mailNickname", "mail", "onPremisesSecurityIdentifier" };
                    }).GetAwaiter().GetResult();

                if (group != null)
                {
                    return new UserElementApiModel
                    {
                        Username = group.MailNickname,
                        DisplayName = group.DisplayName,
                        Pid = group.Id,
                        Sid = group.OnPremisesSecurityIdentifier,
                        IsGroup = true,
                        Email = group.Mail
                    };
                }
            }
            catch (ApiException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
            {
                // Fall through to SID-shape group fallback
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error getting entity from Azure Entra ID");
                throw;
            }

            // P-4 group fallback: SID-shaped input — try onPremisesSecurityIdentifier filter
            if (isSidShaped)
            {
                var hit = FindGroupByOnPremisesSid(graphClient, pid);
                if (hit != null) return hit;
            }

            throw new ArgumentException($"Failed to locate an entity with ID: {pid}");
        }

        // P-4 helper: resolve a synced-from-AD user via the onPremisesSecurityIdentifier filter.
        // Returns null if no enabled match is found.
        private UserElementApiModel? FindUserByOnPremisesSid(GraphServiceClient graphClient, string sid)
        {
            try
            {
                var safe = EscapeODataString(sid);
                var users = graphClient.Users
                    .GetAsync(req =>
                    {
                        req.Headers.Add("ConsistencyLevel", "eventual");
                        req.QueryParameters.Count = true;
                        req.QueryParameters.Filter = $"onPremisesSecurityIdentifier eq '{safe}'";
                        req.QueryParameters.Select = new[]
                        {
                            "id", "displayName", "userPrincipalName", "mail", "accountEnabled", "onPremisesSecurityIdentifier"
                        };
                    }).GetAwaiter().GetResult();

                var hit = users?.Value?.FirstOrDefault(u => u.AccountEnabled == true);
                if (hit == null) return null;

                return new UserElementApiModel
                {
                    Username = hit.UserPrincipalName,
                    DisplayName = hit.DisplayName,
                    Pid = hit.Id,
                    Sid = hit.OnPremisesSecurityIdentifier ?? sid,
                    IsGroup = false,
                    Email = hit.Mail ?? hit.UserPrincipalName
                };
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error resolving user by onPremisesSecurityIdentifier");
                throw;
            }
        }

        // P-4 helper: resolve a synced-from-AD group via the onPremisesSecurityIdentifier filter.
        private UserElementApiModel? FindGroupByOnPremisesSid(GraphServiceClient graphClient, string sid)
        {
            try
            {
                var safe = EscapeODataString(sid);
                var groups = graphClient.Groups
                    .GetAsync(req =>
                    {
                        req.Headers.Add("ConsistencyLevel", "eventual");
                        req.QueryParameters.Count = true;
                        req.QueryParameters.Filter = $"onPremisesSecurityIdentifier eq '{safe}'";
                        req.QueryParameters.Select = new[]
                        {
                            "id", "displayName", "mailNickname", "mail", "onPremisesSecurityIdentifier"
                        };
                    }).GetAwaiter().GetResult();

                var hit = groups?.Value?.FirstOrDefault();
                if (hit == null) return null;

                return new UserElementApiModel
                {
                    Username = hit.MailNickname,
                    DisplayName = hit.DisplayName,
                    Pid = hit.Id,
                    Sid = hit.OnPremisesSecurityIdentifier ?? sid,
                    IsGroup = true,
                    Email = hit.Mail
                };
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error resolving group by onPremisesSecurityIdentifier");
                throw;
            }
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
                        requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
                        requestConfiguration.QueryParameters.Count = true;
                        requestConfiguration.QueryParameters.Filter =
                            $"startsWith(displayName,'{safeUsername}') or startsWith(mail,'{safeUsername}') or " +
                            $"startsWith(onPremisesSamAccountName,'{safeUsername}') or " +
                            $"startsWith(userPrincipalName,'{safeUsername}')";
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "userPrincipalName", "mail", "accountEnabled", "onPremisesSecurityIdentifier" };
                    }).GetAwaiter().GetResult();

                var activeUser = users?.Value?.FirstOrDefault(u => u.AccountEnabled == true);
                if (activeUser != null)
                {
                    return new UserElementApiModel
                    {
                        Username = activeUser.UserPrincipalName,
                        DisplayName = activeUser.DisplayName,
                        Pid = activeUser.Id,
                        Sid = activeUser.OnPremisesSecurityIdentifier,
                        IsGroup = false,
                        Email = activeUser.Mail ?? activeUser.UserPrincipalName
                    };
                }
            }
            catch (ApiException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
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

        // P-7: emits both Entra group IDs and their onPremisesSecurityIdentifier values so
        // downstream EF queries that key off either Pid or Sid (e.g. EnvironmentsPersistentSource
        // line 932 — `ac.Sid OR ac.Pid`) keep matching after the AD→Graph migration.
        public List<string> GetSidsForUser(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                throw new ArgumentException("User ID cannot be null or empty.");
            }

            var result = new List<string> { userId };
            var graphClient = GetGraphClient();

            try
            {
                // Append the caller's own on-prem SID, if any — supports authz against legacy
                // AccessControl.Sid rows that name the user directly (rather than a group).
                var self = graphClient.Users[userId]
                    .GetAsync(req =>
                    {
                        req.QueryParameters.Select = new[] { "id", "onPremisesSecurityIdentifier" };
                    }).GetAwaiter().GetResult();

                if (!string.IsNullOrEmpty(self?.OnPremisesSecurityIdentifier))
                {
                    result.Add(self.OnPremisesSecurityIdentifier);
                }
            }
            catch (Exception ex)
            {
                // Logged but not fatal — the user-self lookup is a best-effort enrichment.
                _log.LogWarning(ex, "Unable to resolve onPremisesSecurityIdentifier for user {UserId}", userId);
            }

            try
            {
                // Transitive group memberships — emits both `id` (Pid) and on-prem SID (Sid).
                var memberOf = graphClient.Users[userId].TransitiveMemberOf.GraphGroup
                    .GetAsync(req =>
                    {
                        req.QueryParameters.Select = new[] { "id", "onPremisesSecurityIdentifier" };
                    }).GetAwaiter().GetResult();

                if (memberOf?.Value != null)
                {
                    foreach (var group in memberOf.Value)
                    {
                        if (!string.IsNullOrEmpty(group.Id))
                        {
                            result.Add(group.Id);
                        }
                        if (!string.IsNullOrEmpty(group.OnPremisesSecurityIdentifier))
                        {
                            result.Add(group.OnPremisesSecurityIdentifier);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error getting transitive group memberships from Azure Entra ID");
                throw;
            }

            return result;
        }

        // P-5: resolves the caller's `userName` argument — which may arrive as a bare
        // sAMAccountName, a `DOMAIN\sAMAccountName`, or a UPN — to an Entra object id
        // before invoking the transitive membership check. `domainName` is intentionally
        // ignored: DORC's Entra setup is single-tenant per install, and cross-forest
        // foreign security principals are out of parity (HLPS §4).
        public string? GetGroupSidIfUserIsMemberRecursive(string userName, string groupName, string domainName)
        {
            var graphClient = GetGraphClient();

            try
            {
                var resolvedUserId = ResolveUserIdFromName(graphClient, userName);
                if (string.IsNullOrEmpty(resolvedUserId))
                {
                    return string.Empty;
                }

                var safeGroup = EscapeODataString(groupName);
                var group = graphClient.Groups
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Filter =
                            $"displayName eq '{safeGroup}' or mailNickname eq '{safeGroup}'";
                        requestConfiguration.QueryParameters.Select = new[] { "id" };
                    }).GetAwaiter().GetResult();

                var targetGroup = group?.Value?.FirstOrDefault();
                if (targetGroup == null)
                {
                    return string.Empty;
                }

                var requestBody = new CheckMemberGroupsPostRequestBody
                {
                    GroupIds = new List<string> { targetGroup.Id! }
                };

                var isMember = graphClient.Users[resolvedUserId].CheckMemberGroups
                    .PostAsCheckMemberGroupsPostResponseAsync(requestBody).Result;

                if (isMember?.Value != null && isMember.Value.Any())
                {
                    return targetGroup.Id;
                }
            }
            catch (ApiException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.Forbidden)
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

        // P-5 helper: normalises caller input (DOMAIN\name, name(External), bare name, UPN)
        // and resolves to an Entra object id via onPremisesSamAccountName/userPrincipalName filter.
        private string? ResolveUserIdFromName(GraphServiceClient graphClient, string userName)
        {
            if (string.IsNullOrWhiteSpace(userName)) return null;

            var name = userName.Trim();

            // Strip DOMAIN\ prefix (Negotiate-style identity). The last \-segment wins.
            var backslash = name.LastIndexOf('\\');
            if (backslash >= 0)
            {
                name = name[(backslash + 1)..];
            }

            // Strip a trailing (External) marker if present.
            const string externalSuffix = "(External)";
            if (name.EndsWith(externalSuffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^externalSuffix.Length].Trim();
            }

            if (string.IsNullOrEmpty(name)) return null;

            var safe = EscapeODataString(name);
            var users = graphClient.Users
                .GetAsync(req =>
                {
                    req.Headers.Add("ConsistencyLevel", "eventual");
                    req.QueryParameters.Count = true;
                    req.QueryParameters.Filter =
                        $"onPremisesSamAccountName eq '{safe}' or userPrincipalName eq '{safe}'";
                    req.QueryParameters.Select = new[] { "id" };
                }).GetAwaiter().GetResult();

            return users?.Value?.FirstOrDefault()?.Id;
        }
    }
}
