using Azure.Identity;
using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Authentication;
using Microsoft.Graph.Users.Item.CheckMemberGroups;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using System.Text.RegularExpressions;

namespace Dorc.Core
{
    public class PrincipalDirectory : IPrincipalDirectory, IDisposable
    {
        // Matches well-formed Windows/AD SIDs (S-1-5-..., S-1-12-...). Used to decide
        // whether to fall back to onPremisesSecurityIdentifier filter queries on direct-lookup 404.
        private static readonly Regex AdSidShape = new("^S-1-(5|12)-\\d+(-\\d+)*$", RegexOptions.Compiled);

        // Graph's maximum $top for directory collections; keeps the page count low when
        // draining nextLink.
        private const int MaxPageSize = 999;

        // Hard cap on nextLink follows. At MaxPageSize this covers ~50k group memberships —
        // far beyond any real principal — while guaranteeing the drain loop terminates even
        // if Graph returns a self-referential or endlessly repeating nextLink.
        private const int MaxPages = 50;

        // Page cap for the people picker. 10 x MaxPageSize comfortably exceeds the ~1000 the
        // AD path returned, while bounding a runaway prefix search in a very large tenant.
        private const int MaxSearchPages = 10;

        private readonly string _tenantId;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly ILogger _log;
        private readonly Func<GraphServiceClient>? _graphClientFactory;
        private GraphServiceClient? _graphClient;
        private RetryHandler? _customRetryHandler;

        public PrincipalDirectory(IConfigurationSettings config, ILogger<PrincipalDirectory> log)
        {
            _tenantId = config.GetAzureEntraTenantId();
            _clientId = config.GetAzureEntraClientId();
            _clientSecret = config.GetAzureEntraClientSecret();

            _log = log;
        }

        // Test-seam ctor: injects a pre-built GraphServiceClient so tests can drive a fake.
        // Per SPEC-S-001 §3.1 — the only way to satisfy HLPS SC-9 (integration-level Graph-fake tests).
        internal PrincipalDirectory(Func<GraphServiceClient> graphClientFactory, ILogger<PrincipalDirectory> log)
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

                // Retry/backoff on throttling and transient 5xx. The composite searcher used to
                // absorb a failing backend and return partial results; with one searcher a Graph
                // 429 or 503 otherwise surfaces to the user as a 500. RetryHandler honours the
                // Retry-After header Graph sends with 429.
                var retryOption = new RetryHandlerOption
                {
                    MaxRetry = 3,
                    Delay = 2,
                    ShouldRetry = (_, _, response) =>
                        response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                        || response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable
                        || response.StatusCode == System.Net.HttpStatusCode.GatewayTimeout
                };

                var handlers = GraphClientFactory.CreateDefaultHandlers();
                var defaultRetry = handlers.OfType<RetryHandler>().FirstOrDefault();
                if (defaultRetry != null)
                {
                    handlers.Remove(defaultRetry);
                }
                _customRetryHandler?.Dispose();
                _customRetryHandler = new RetryHandler(retryOption);
                handlers.Insert(0, _customRetryHandler);

                var httpClient = GraphClientFactory.Create(handlers);
                _graphClient = new GraphServiceClient(httpClient, authProvider);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error initializing GraphServiceClient.");
                throw new InvalidOperationException("Failed to initialize GraphServiceClient.", ex);
            }

            return _graphClient;
        }

        private static string EscapeODataString(string s) => s?.Replace("'", "''") ?? string.Empty;

        // Allowed characters for a directory search term. NOTE: the hyphen is intentionally last
        // so it is a literal, not a range. A previous version used "'-_" which is a character
        // RANGE (0x27-0x5F) that silently permitted metacharacters such as * ( ) and \.
        // Carried over from ActiveDirectorySearcher, which this class replaces: the LDAP filter
        // is gone but the input validation it guarded is transport-agnostic and still required.
        internal static bool IsValidSearchName(string name)
        {
            // \A..\z rather than ^..$: in .NET, $ also matches immediately before a trailing
            // newline, so "alice\n" would otherwise pass and carry the \n into the OData filter.
            return name != null && Regex.IsMatch(name, @"\A[a-zA-Z0-9'_. -]+(\(External\))?\z");
        }

        // Graph accepts an object id (GUID) or a UPN as a /users/{x} path segment. A bare
        // sAMAccountName is neither and yields 400, so callers must resolve it first.
        private static bool LooksLikeGraphUserIdentifier(string value)
        {
            return Guid.TryParse(value, out _) || value.Contains('@');
        }

        // Broad-search guard. Deliberately wider than IsValidSearchName: this backs the
        // free-text people picker, so it must accept everything DirectorySearchController's
        // own contract accepts (letters, digits, - _ . ' space ( ) &) plus non-ASCII names
        // such as "Müller" or "Łukasz" — hence \p{L}/\p{N} rather than a-zA-Z0-9.
        // AccessControlController.SearchUsers performs no validation of its own, so this is
        // the only structural guard on that path.
        internal static bool IsValidSearchTerm(string term)
        {
            return term != null && Regex.IsMatch(term, @"\A[\p{L}\p{N}_.'\- ()&]+\z");
        }

        public List<DirectoryPrincipalApiModel> Search(string objectName)
        {
            var output = new List<DirectoryPrincipalApiModel>();

            // Reject structurally malformed search terms rather than round-tripping them to
            // Graph. Returns empty (not throw), matching the replaced ActiveDirectorySearcher.
            if (!IsValidSearchTerm(objectName))
            {
                return output;
            }

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
                        requestConfiguration.QueryParameters.Top = MaxPageSize;
                        requestConfiguration.QueryParameters.Filter =
                            $"accountEnabled eq true and (" +
                            $"startsWith(displayName,'{objectName}') or startsWith(givenName,'{objectName}') or " +
                            $"startsWith(onPremisesSamAccountName,'{objectName}') or " +
                            $"startsWith(surname,'{objectName}') or startsWith(mail,'{objectName}') or " +
                            $"startsWith(userPrincipalName,'{objectName}'))";
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "userPrincipalName", "mail", "accountEnabled", "onPremisesSamAccountName", "onPremisesSecurityIdentifier" };
                    }).GetAwaiter().GetResult();

                // Drain every page. The replaced DirectorySearcher.FindAll() returned up to the
                // server limit (~1000); reading one 100-row Graph page silently truncated the
                // people picker in any large tenant with no signal to the caller.
                var userPages = 0;
                while (users?.Value != null)
                {
                    foreach (var user in users.Value)
                    {
                        output.Add(new DirectoryPrincipalApiModel
                        {
                            Username = user.UserPrincipalName,
                            DisplayName = user.DisplayName,
                            PrincipalId = user.Id,
                            OnPremisesSid = user.OnPremisesSecurityIdentifier,
                            SamAccountName = user.OnPremisesSamAccountName,
                            IsGroup = false,
                            Email = user.Mail ?? user.UserPrincipalName
                        });
                    }

                    if (string.IsNullOrEmpty(users.OdataNextLink) || ++userPages >= MaxSearchPages) break;
                    users = graphClient.Users.WithUrl(users.OdataNextLink)
                        .GetAsync().GetAwaiter().GetResult();
                }

                // Search for groups
                var groups = graphClient.Groups
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
                        requestConfiguration.QueryParameters.Count = true;
                        requestConfiguration.QueryParameters.Top = MaxPageSize;
                        requestConfiguration.QueryParameters.Filter =
                            $"startsWith(displayName,'{objectName}') or startsWith(mailNickname,'{objectName}') or " +
                            $"startsWith(onPremisesSamAccountName, '{objectName}')";
                        requestConfiguration.QueryParameters.Select =
                            new[] { "id", "displayName", "mailNickname", "mail", "onPremisesSamAccountName", "onPremisesSecurityIdentifier" };
                    }).GetAwaiter().GetResult();

                var groupPages = 0;
                while (groups?.Value != null)
                {
                    foreach (var group in groups.Value)
                    {
                        output.Add(new DirectoryPrincipalApiModel
                        {
                            Username = group.MailNickname,
                            DisplayName = group.DisplayName,
                            PrincipalId = group.Id,
                            OnPremisesSid = group.OnPremisesSecurityIdentifier,
                            SamAccountName = group.OnPremisesSamAccountName,
                            IsGroup = true,
                            Email = group.Mail
                        });
                    }

                    if (string.IsNullOrEmpty(groups.OdataNextLink) || ++groupPages >= MaxSearchPages) break;
                    groups = graphClient.Groups.WithUrl(groups.OdataNextLink)
                        .GetAsync().GetAwaiter().GetResult();
                }

                // Service principals (M2M clients). Deleting IdentityServerSearcher removed the
                // only searcher that surfaced machine clients in the ACL picker, so new M2M
                // grants could not be made through the UI. Their appId is what
                // OAuthClaimsPrincipalReader matches against AccessControl.Pid for M2M callers,
                // so that is what we surface as Pid. Requires Application.Read.All.
AppendServicePrincipals(graphClient, objectName, output);
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

        public DirectoryPrincipalApiModel FindById(string pid)
        {
            if (string.IsNullOrWhiteSpace(pid))
            {
                throw new ArgumentException("The ID cannot be null or empty.");
            }

            var graphClient = GetGraphClient();
            var isSidShaped = AdSidShape.IsMatch(pid);

            // Direct user lookup. Skipped for SID-shaped input: Graph treats a non-GUID
            // segment as a UPN and answers 400 Request_BadRequest (NOT 404) for an AD SID,
            // so probing here would throw past the fallback below rather than fall through.
            if (!isSidShaped)
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
                    return new DirectoryPrincipalApiModel
                    {
                        Username = user.UserPrincipalName,
                        DisplayName = user.DisplayName,
                        PrincipalId = user.Id,
                        OnPremisesSid = user.OnPremisesSecurityIdentifier,
                        IsGroup = false,
                        Email = user.Mail ?? user.UserPrincipalName
                    };
                }
            }
            catch (ApiException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound
                                          || ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.BadRequest)
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

            // Direct group lookup — skipped for SID-shaped input, same 400 reason as above.
            if (!isSidShaped)
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
                    return new DirectoryPrincipalApiModel
                    {
                        Username = group.MailNickname,
                        DisplayName = group.DisplayName,
                        PrincipalId = group.Id,
                        OnPremisesSid = group.OnPremisesSecurityIdentifier,
                        IsGroup = true,
                        Email = group.Mail
                    };
                }
            }
            catch (ApiException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound
                                          || ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.BadRequest)
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
        private DirectoryPrincipalApiModel? FindUserByOnPremisesSid(GraphServiceClient graphClient, string sid)
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

                return new DirectoryPrincipalApiModel
                {
                    Username = hit.UserPrincipalName,
                    DisplayName = hit.DisplayName,
                    PrincipalId = hit.Id,
                    OnPremisesSid = hit.OnPremisesSecurityIdentifier ?? sid,
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
        private DirectoryPrincipalApiModel? FindGroupByOnPremisesSid(GraphServiceClient graphClient, string sid)
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

                return new DirectoryPrincipalApiModel
                {
                    Username = hit.MailNickname,
                    DisplayName = hit.DisplayName,
                    PrincipalId = hit.Id,
                    OnPremisesSid = hit.OnPremisesSecurityIdentifier ?? sid,
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

        public DirectoryPrincipalApiModel FindByName(string username)
        {
            if (!IsValidSearchName(username))
            {
                throw new ArgumentException("Invalid search criteria. Search criteria must match \"^[a-zA-Z0-9'_. -]+(\\(External\\))?$\"!");
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
                    return new DirectoryPrincipalApiModel
                    {
                        Username = activeUser.UserPrincipalName,
                        DisplayName = activeUser.DisplayName,
                        PrincipalId = activeUser.Id,
                        OnPremisesSid = activeUser.OnPremisesSecurityIdentifier,
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
        public List<string> GetIdentifiersForUser(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                throw new ArgumentException("User ID cannot be null or empty.");
            }

            var result = new List<string> { userId };
            var graphClient = GetGraphClient();

            // WinAuthClaimsPrincipalReader passes a bare sAMAccountName here (and
            // ClaimsPrincipalReaderFactory a sAMAccountName-or-email when
            // IsUseAdSidsForAccessControl is set). Graph only accepts an object id or a UPN
            // as a path segment and answers 400 for anything else, so resolve first —
            // the same step FindGroupIfMember already performs.
            var graphUserId = LooksLikeGraphUserIdentifier(userId)
                ? userId
                : ResolveUserIdFromName(graphClient, userId);

            if (string.IsNullOrEmpty(graphUserId))
            {
                // Unresolvable caller: return the raw input only. Every consumer treats a
                // short list as deny, so this fails closed rather than throwing a 500.
                _log.LogWarning("Unable to resolve the requested user to a directory object id");
                return result;
            }

            try
            {
                // Append the caller's own on-prem SID, if any — supports authz against legacy
                // AccessControl.Sid rows that name the user directly (rather than a group).
                var self = graphClient.Users[graphUserId]
                    .GetAsync(req =>
                    {
                        req.QueryParameters.Select = new[] { "id", "onPremisesSecurityIdentifier" };
                    }).GetAwaiter().GetResult();

                if (!string.IsNullOrEmpty(self?.OnPremisesSecurityIdentifier))
                {
                    result.Add(self.OnPremisesSecurityIdentifier);
                }
            }
            catch (ApiException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
            {
                // Genuinely absent (cloud-only account) — not fatal, this is enrichment.
                // Caller identity is intentionally omitted from the log message: CodeQL flagged
                // it as PII when reached via authenticated call paths (e.g. GetUserEmail upstream).
                _log.LogWarning(ex, "No onPremisesSecurityIdentifier for the requested user");
            }

            try
            {
                // Transitive group memberships — emits both `id` (Pid) and on-prem SID (Sid).
                var memberOf = graphClient.Users[graphUserId].TransitiveMemberOf.GraphGroup
                    .GetAsync(req =>
                    {
                        req.QueryParameters.Top = MaxPageSize;
                        req.QueryParameters.Select = new[] { "id", "onPremisesSecurityIdentifier" };
                    }).GetAwaiter().GetResult();

                // transitiveMemberOf is a PAGED collection (the replaced getMemberGroups
                // action was not). Without draining nextLink a user in more than one page of
                // groups silently loses ACL grants — and CachedUserGroupReader caches that.
                var pagesRead = 0;
                while (memberOf?.Value != null)
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

                    if (string.IsNullOrEmpty(memberOf.OdataNextLink))
                    {
                        break;
                    }

                    if (++pagesRead >= MaxPages)
                    {
                        // Fail loudly: silently returning a truncated authorization list is
                        // exactly the defect this drain loop exists to prevent.
                        throw new InvalidOperationException(
                            $"Transitive group membership exceeded {MaxPages} pages; refusing to return a truncated authorization list.");
                    }

                    memberOf = graphClient.Users[graphUserId].TransitiveMemberOf.GraphGroup
                        .WithUrl(memberOf.OdataNextLink)
                        .GetAsync().GetAwaiter().GetResult();
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
        public string? FindGroupIfMember(string userName, string groupName, string domainName)
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
                    .PostAsCheckMemberGroupsPostResponseAsync(requestBody).GetAwaiter().GetResult();

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

        // Service principals (M2M clients). Deleting IdentityServerSearcher removed the only
        // searcher that surfaced machine clients in the ACL picker, so new M2M grants could not
        // be made through the UI. Their appId is what OAuthClaimsPrincipalReader matches against
        // AccessControl.Pid for M2M callers, so that is what we surface as Pid.
        //
        // Deliberately non-fatal: this needs Application.Read.All, a permission existing tenants
        // have not consented to. A 403 here must degrade to "no machine clients in the results",
        // not take down user and group search with it.
        private void AppendServicePrincipals(GraphServiceClient graphClient, string escapedName, List<DirectoryPrincipalApiModel> output)
        {
            try
            {
                var principals = graphClient.ServicePrincipals
                    .GetAsync(req =>
                    {
                        req.Headers.Add("ConsistencyLevel", "eventual");
                        req.QueryParameters.Count = true;
                        req.QueryParameters.Top = MaxPageSize;
                        req.QueryParameters.Filter =
                            $"startsWith(displayName,'{escapedName}') or startsWith(appId,'{escapedName}')";
                        req.QueryParameters.Select = new[] { "id", "appId", "displayName", "accountEnabled" };
                    }).GetAwaiter().GetResult();

                var pages = 0;
                while (principals?.Value != null)
                {
                    foreach (var sp in principals.Value.Where(sp => sp.AccountEnabled != false))
                    {
                        output.Add(new DirectoryPrincipalApiModel
                        {
                            Username = sp.AppId,
                            DisplayName = sp.DisplayName,
                            PrincipalId = sp.AppId,
                            IsGroup = false
                        });
                    }

                    if (string.IsNullOrEmpty(principals.OdataNextLink) || ++pages >= MaxSearchPages) break;
                    principals = graphClient.ServicePrincipals.WithUrl(principals.OdataNextLink)
                        .GetAsync().GetAwaiter().GetResult();
                }
            }
            catch (ApiException ex)
            {
                // Deliberately non-fatal, and narrowed to the Graph request layer: a failed
                // enrichment must not sink the whole search, but a non-Graph bug should surface.
                _log.LogWarning(ex,
                    "Unable to search service principals; machine clients will be absent from the results. " +
                    "This usually means the app registration lacks Application.Read.All.");
            }
        }

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
                    // Ask for 2 so an ambiguous name is detectable rather than silently
                    // collapsed by FirstOrDefault.
                    req.QueryParameters.Top = 2;
                    req.QueryParameters.Filter =
                        $"accountEnabled eq true and (onPremisesSamAccountName eq '{safe}' or userPrincipalName eq '{safe}')";
                    req.QueryParameters.Select = new[] { "id" };
                }).GetAwaiter().GetResult();

            var matches = users?.Value;
            if (matches == null || matches.Count == 0)
            {
                return null;
            }

            // onPremisesSamAccountName is unique within a DOMAIN, not within a tenant. When
            // several on-prem domains sync into one tenant the same name can resolve to more
            // than one principal, and picking either would bind the caller to the wrong
            // identity's group claims. Refuse instead of guessing.
            if (matches.Count > 1)
            {
                _log.LogError("Directory name resolved to multiple principals; refusing to guess an identity");
                return null;
            }

            return matches[0].Id;
        }

        public void Dispose()
        {
            // The Graph HttpClient pipeline also disposes its handlers; HttpResponseMessage-
            // style double disposal is safe here because DelegatingHandler.Dispose is idempotent.
            _customRetryHandler?.Dispose();
            _customRetryHandler = null;
            GC.SuppressFinalize(this);
        }
    }
}
