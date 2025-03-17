using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System.Diagnostics;
using System.DirectoryServices.Protocols;
using System.Net.Security;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

/// <summary>
/// Authenticates requests using Negotiate, Kerberos, or NTLM.
/// </summary>
public class AuthNegotiateHandler : AuthenticationHandler<AuthNegotiateOptions>, IAuthenticationRequestHandler
{
    private const string AuthPersistenceKey = nameof(AuthPersistence);
    private const string NegotiateVerb = "Negotiate";
    private const string AuthHeaderPrefix = NegotiateVerb + " ";

    private bool _requestProcessed;
    private INegotiateState? _negotiateState;

    /// <summary>
    /// Creates a new <see cref="NegotiateHandler"/>
    /// </summary>
    /// <inheritdoc />
    [Obsolete("ISystemClock is obsolete, use TimeProvider on AuthenticationSchemeOptions instead.")]
    public AuthNegotiateHandler(IOptionsMonitor<AuthNegotiateOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock)
    { }

    /// <summary>
    /// Creates a new <see cref="NegotiateHandler"/>
    /// </summary>
    /// <inheritdoc />
    public AuthNegotiateHandler(IOptionsMonitor<AuthNegotiateOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    { }

    /// <summary>
    /// The handler calls methods on the events which give the application control at certain points where processing is occurring.
    /// If it is not provided a default instance is supplied which does nothing when the methods are called.
    /// </summary>
    protected new AuthNegotiateEvents Events
    {
        get => (AuthNegotiateEvents)base.Events!;
        set => base.Events = value;
    }

    /// <summary>
    /// Creates the default events type.
    /// </summary>
    /// <returns></returns>
    protected override Task<object> CreateEventsAsync() => Task.FromResult<object>(new AuthNegotiateEvents());

    private bool IsSupportedProtocol => HttpProtocol.IsHttp11(Request.Protocol) || HttpProtocol.IsHttp10(Request.Protocol);

    /// <summary>
    /// Intercepts incomplete Negotiate authentication handshakes and continues or completes them.
    /// </summary>
    /// <returns><see langword="true" /> if a response was generated, otherwise <see langword="false"/>.</returns>
    public async Task<bool> HandleRequestAsync()
    {
        AuthPersistence? persistence = null;
        bool authFailedEventCalled = false;
        try
        {
            if (_requestProcessed || Options.DeferToServer)
            {
                // This request was already processed but something is re-executing it like an exception handler.
                // Don't re-run because we could corrupt the connection state, e.g. if this was a stage2 NTLM request
                // that we've already completed the handshake for.
                // Or we're in deferral mode where we let the server handle the authentication.
                return false;
            }

            _requestProcessed = true;

            if (!IsSupportedProtocol)
            {
                // HTTP/1.0 and HTTP/1.1 are supported. Do not throw because this may be running on a server that supports
                // additional protocols.
                return false;
            }

            var connectionItems = GetConnectionItems();
            persistence = (AuthPersistence)connectionItems[AuthPersistenceKey]!;
            _negotiateState = persistence?.State;

            var authorizationHeader = Request.Headers.Authorization;

            if (StringValues.IsNullOrEmpty(authorizationHeader))
            {
                if (_negotiateState?.IsCompleted == false)
                {
                    throw new InvalidOperationException("An anonymous request was received in between authentication handshake requests.");
                }
                return false;
            }

            var authorization = authorizationHeader.ToString();
            string? token = null;
            if (authorization.StartsWith(AuthHeaderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                token = authorization.Substring(AuthHeaderPrefix.Length).Trim();
            }
            else
            {
                if (_negotiateState?.IsCompleted == false)
                {
                    throw new InvalidOperationException("Non-negotiate request was received in between authentication handshake requests.");
                }
                return false;
            }

            // WinHttpHandler re-authenticates an existing connection if it gets another challenge on subsequent requests.
            if (_negotiateState?.IsCompleted == true)
            {
                Logger.Reauthenticating();
                _negotiateState.Dispose();
                _negotiateState = null;
                if (persistence != null)
                {
                    persistence.State = null;
                }
            }

            _negotiateState ??= new AuthNegotiateState();

            var outgoing = _negotiateState.GetOutgoingBlob(token, out var errorType, out var exception);
            if (errorType != BlobErrorType.None)
            {
                Debug.Assert(exception != null);

                Logger.NegotiateError(errorType.ToString());
                _negotiateState.Dispose();
                _negotiateState = null;
                if (persistence?.State != null)
                {
                    persistence.State.Dispose();
                    persistence.State = null;
                }

                if (errorType == BlobErrorType.CredentialError)
                {
                    Logger.CredentialError(exception);
                    authFailedEventCalled = true; // Could throw, and we don't want to double trigger the event.
                    var result = await InvokeAuthenticateFailedEvent(exception);
                    return result ?? false; // Default to skipping the handler, let AuthZ generate a new 401
                }
                else if (errorType == BlobErrorType.ClientError)
                {
                    Logger.ClientError(exception);
                    authFailedEventCalled = true; // Could throw, and we don't want to double trigger the event.
                    var result = await InvokeAuthenticateFailedEvent(exception);
                    if (result.HasValue)
                    {
                        return result.Value;
                    }
                    Context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return true; // Default to terminating request
                }

                throw exception;
            }

            if (!_negotiateState.IsCompleted)
            {
                persistence ??= EstablishConnectionPersistence(connectionItems);
                // Save the state long enough to complete the multi-stage handshake.
                // We'll remove it once complete if !PersistNtlm/KerberosCredentials.
                persistence.State = _negotiateState;

                Logger.IncompleteNegotiateChallenge();
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                Response.Headers.Append(HeaderNames.WWWAuthenticate, AuthHeaderPrefix + outgoing);
                return true;
            }

            Logger.NegotiateComplete();

            // There can be a final blob of data we need to send to the client, but let the request execute as normal.
            if (!string.IsNullOrEmpty(outgoing))
            {
                Response.OnStarting(() =>
                {
                    // Only include it if the response ultimately succeeds. This avoids adding it twice if Challenge is called again.
                    if (Response.StatusCode < StatusCodes.Status400BadRequest)
                    {
                        Response.Headers.Append(HeaderNames.WWWAuthenticate, AuthHeaderPrefix + outgoing);
                    }
                    return Task.CompletedTask;
                });
            }

            // Deal with connection credential persistence.

            if (_negotiateState.Protocol == "NTLM" && !Options.PersistNtlmCredentials)
            {
                // NTLM was already put in the persitence cache on the prior request so we could complete the handshake.
                // Take it out if we don't want it to persist.
                Debug.Assert(object.ReferenceEquals(persistence?.State, _negotiateState),
                    "NTLM is a two stage process, it must have already been in the cache for the handshake to succeed.");
                Logger.DisablingCredentialPersistence(_negotiateState.Protocol);
                persistence.State = null;
                Response.RegisterForDispose(_negotiateState);
            }
            else if (_negotiateState.Protocol == "Kerberos")
            {
                // Kerberos can require one or two stage handshakes
                if (Options.PersistKerberosCredentials)
                {
                    Logger.EnablingCredentialPersistence();
                    persistence ??= EstablishConnectionPersistence(connectionItems);
                    persistence.State = _negotiateState;
                }
                else
                {
                    if (persistence?.State != null)
                    {
                        Logger.DisablingCredentialPersistence(_negotiateState.Protocol);
                        persistence.State = null;
                    }
                    Response.RegisterForDispose(_negotiateState);
                }
            }

            // Note we run the Authenticated event in HandleAuthenticateAsync so it is per-request rather than per connection.
        }
        catch (Exception ex)
        {
            if (authFailedEventCalled)
            {
                throw;
            }

            Logger.ExceptionProcessingAuth(ex);

            // Clear state so it's possible to retry on the same connection.
            _negotiateState?.Dispose();
            _negotiateState = null;
            if (persistence?.State != null)
            {
                persistence.State.Dispose();
                persistence.State = null;
            }

            var result = await InvokeAuthenticateFailedEvent(ex);
            if (result.HasValue)
            {
                return result.Value;
            }

            throw;
        }

        return false;
    }

    private async Task<bool?> InvokeAuthenticateFailedEvent(Exception ex)
    {
        var errorContext = new AuthAuthenticationFailedContext(Context, Scheme, Options) { Exception = ex };
        await Events.AuthenticationFailed(errorContext);

        if (errorContext.Result != null)
        {
            if (errorContext.Result.Handled)
            {
                return true;
            }
            else if (errorContext.Result.Skipped)
            {
                return false;
            }
            else if (errorContext.Result.Failure != null)
            {
                throw new AuthenticationFailureException("An error was returned from the AuthenticationFailed event.", errorContext.Result.Failure);
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if the current request is authenticated and returns the user.
    /// </summary>
    /// <returns></returns>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_requestProcessed)
        {
            throw new InvalidOperationException("AuthenticateAsync must not be called before the UseAuthentication middleware runs.");
        }

        if (!IsSupportedProtocol)
        {
            // Not supported. We don't throw because Negotiate may be set as the default auth
            // handler on a server that's running HTTP/1 and HTTP/2. We'll challenge HTTP/2 requests
            // that require auth and they'll downgrade to HTTP/1.1.
            Logger.ProtocolNotSupported(Request.Protocol);
            return AuthenticateResult.NoResult();
        }

        if (_negotiateState == null)
        {
            return AuthenticateResult.NoResult();
        }

        if (!_negotiateState.IsCompleted)
        {
            // This case should have been rejected by HandleRequestAsync
            throw new InvalidOperationException("Attempting to use an incomplete authentication context.");
        }

        // Make a new copy of the user for each request, they are mutable objects and
        // things like ClaimsTransformation run per request.
        var identity = _negotiateState.GetIdentity();
        ClaimsPrincipal user;
        if (OperatingSystem.IsWindows() && identity is WindowsIdentity winIdentity)
        {
            user = new WindowsPrincipal(winIdentity);
            Response.RegisterForDispose(winIdentity);
        }
        else
        {
            user = new ClaimsPrincipal(new ClaimsIdentity(identity));
        }

        AuthAuthenticatedContext authenticatedContext;

        if (Options.AuthLdapSettings.EnableLdapClaimResolution)
        {
            var ldapContext = new AuthLdapContext(Context, Scheme, Options, Options.AuthLdapSettings)
            {
                Principal = user
            };

            await Events.RetrieveLdapClaims(ldapContext);

            if (ldapContext.Result != null)
            {
                return ldapContext.Result;
            }

            await AuthLdapAdapter.RetrieveClaimsAsync(ldapContext.AuthLdapSettings, (ldapContext.Principal.Identity as ClaimsIdentity)!, Logger);

            authenticatedContext = new AuthAuthenticatedContext(Context, Scheme, Options)
            {
                Principal = ldapContext.Principal
            };
        }
        else
        {
            authenticatedContext = new AuthAuthenticatedContext(Context, Scheme, Options)
            {
                Principal = user
            };
        }

        await Events.Authenticated(authenticatedContext);

        if (authenticatedContext.Result != null)
        {
            return authenticatedContext.Result;
        }

        var ticket = new AuthenticationTicket(authenticatedContext.Principal, authenticatedContext.Properties, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    /// <summary>
    /// Issues a 401 WWW-Authenticate Negotiate challenge.
    /// </summary>
    /// <param name="properties"></param>
    /// <returns></returns>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // We allow issuing a challenge from an HTTP/2 request. Browser clients will gracefully downgrade to HTTP/1.1.
        // SocketHttpHandler will not downgrade (https://github.com/dotnet/corefx/issues/35195), but WinHttpHandler will.
        var eventContext = new AuthChallengeContext(Context, Scheme, Options, properties);
        await Events.Challenge(eventContext);
        if (eventContext.Handled)
        {
            return;
        }

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.Append(HeaderNames.WWWAuthenticate, NegotiateVerb);
        Logger.ChallengeNegotiate();
    }

    private AuthPersistence EstablishConnectionPersistence(IDictionary<object, object?> items)
    {
        Debug.Assert(!items.ContainsKey(AuthPersistenceKey), "This should only be registered once per connection");
        var persistence = new AuthPersistence();
        RegisterForConnectionDispose(persistence);
        items[AuthPersistenceKey] = persistence;
        return persistence;
    }

    private IDictionary<object, object?> GetConnectionItems()
    {
        return Context.Features.Get<IConnectionItemsFeature>()?.Items
            ?? throw new NotSupportedException($"Negotiate authentication requires a server that supports {nameof(IConnectionItemsFeature)} like Kestrel.");
    }

    private void RegisterForConnectionDispose(IDisposable authState)
    {
        var connectionCompleteFeature = Context.Features.Get<IConnectionCompleteFeature>()
            ?? throw new NotSupportedException($"Negotiate authentication requires a server that supports {nameof(IConnectionCompleteFeature)} like Kestrel.");
        connectionCompleteFeature.OnCompleted(DisposeState, authState);
    }

    private static Task DisposeState(object state)
    {
        ((IDisposable)state).Dispose();
        return Task.CompletedTask;
    }

    // This allows us to have one disposal registration per connection and limits churn on the Items collection.
    private sealed class AuthPersistence : IDisposable
    {
        internal INegotiateState? State { get; set; }

        public void Dispose()
        {
            State?.Dispose();
        }
    }
}
internal interface INegotiateState : IDisposable
{
    string? GetOutgoingBlob(string incomingBlob, out BlobErrorType status, out Exception? error);

    bool IsCompleted { get; }

    string Protocol { get; }

    IIdentity GetIdentity();
}

internal enum BlobErrorType
{
    None,
    CredentialError,
    ClientError,
    Other
}
static partial class NegotiateLoggingExtensions
{
    [LoggerMessage(1, LogLevel.Information, "Incomplete Negotiate handshake, sending an additional 401 Negotiate challenge.", EventName = "IncompleteNegotiateChallenge")]
    public static partial void IncompleteNegotiateChallenge(this ILogger logger);

    [LoggerMessage(2, LogLevel.Debug, "Completed Negotiate authentication.", EventName = "NegotiateComplete")]
    public static partial void NegotiateComplete(this ILogger logger);

    [LoggerMessage(3, LogLevel.Debug, "Enabling credential persistence for a complete Kerberos handshake.", EventName = "EnablingCredentialPersistence")]
    public static partial void EnablingCredentialPersistence(this ILogger logger);

    [LoggerMessage(4, LogLevel.Debug, "Disabling credential persistence for a complete {protocol} handshake.", EventName = "DisablingCredentialPersistence")]
    public static partial void DisablingCredentialPersistence(this ILogger logger, string protocol);

    [LoggerMessage(5, LogLevel.Error, "An exception occurred while processing the authentication request.", EventName = "ExceptionProcessingAuth")]
    public static partial void ExceptionProcessingAuth(this ILogger logger, Exception ex);

    [LoggerMessage(6, LogLevel.Debug, "Challenged 401 Negotiate.", EventName = "ChallengeNegotiate")]
    public static partial void ChallengeNegotiate(this ILogger logger);

    [LoggerMessage(7, LogLevel.Debug, "Negotiate data received for an already authenticated connection, Re-authenticating.", EventName = "Reauthenticating")]
    public static partial void Reauthenticating(this ILogger logger);

    [LoggerMessage(8, LogLevel.Information, "Deferring to the server's implementation of Windows Authentication.", EventName = "Deferring")]
    public static partial void Deferring(this ILogger logger);

    [LoggerMessage(9, LogLevel.Debug, "There was a problem with the users credentials.", EventName = "CredentialError")]
    public static partial void CredentialError(this ILogger logger, Exception ex);

    [LoggerMessage(10, LogLevel.Debug, "The users authentication request was invalid.", EventName = "ClientError")]
    public static partial void ClientError(this ILogger logger, Exception ex);

    [LoggerMessage(11, LogLevel.Debug, "Negotiate error code: {error}.", EventName = "NegotiateError")]
    public static partial void NegotiateError(this ILogger logger, string error);

    [LoggerMessage(12, LogLevel.Debug, "Negotiate is not supported with {protocol}.", EventName = "ProtocolNotSupported")]
    public static partial void ProtocolNotSupported(this ILogger logger, string protocol);
}

public class AuthNegotiateOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The object provided by the application to process events raised by the negotiate authentication handler.
    /// The application may use the existing NegotiateEvents instance and assign delegates only to the events it
    /// wants to process. The application may also replace it with its own derived instance.
    /// </summary>
    public new AuthNegotiateEvents? Events
    {
        get { return (AuthNegotiateEvents?)base.Events; }
        set { base.Events = value; }
    }

    /// <summary>
    /// Indicates if Kerberos credentials should be persisted and re-used for subsquent anonymous requests.
    /// This option must not be used if connections may be shared by requests from different users.
    /// </summary>
    /// <value>Defaults to <see langword="false"/>.</value>
    public bool PersistKerberosCredentials { get; set; }

    /// <summary>
    /// Indicates if NTLM credentials should be persisted and re-used for subsquent anonymous requests.
    /// This option must not be used if connections may be shared by requests from different users.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool PersistNtlmCredentials { get; set; } = true;

    /// <summary>
    /// Configuration settings for LDAP connections used to retrieve claims.
    /// This should only be used on Linux systems.
    /// </summary>
    internal AuthLdapSettings AuthLdapSettings { get; } = new AuthLdapSettings();

    /// <summary>
    /// Use LDAP connections used to retrieve claims for the given domain.
    /// This should only be used on Linux systems.
    /// </summary>
    public void EnableLdap(string domain)
    {
        ArgumentException.ThrowIfNullOrEmpty(domain);

        AuthLdapSettings.EnableLdapClaimResolution = true;
        AuthLdapSettings.Domain = domain;
    }

    /// <summary>
    /// Use LDAP connections used to retrieve claims using the configured settings.
    /// This should only be used on Linux systems.
    /// </summary>
    public void EnableLdap(Action<AuthLdapSettings> configureSettings)
    {
        ArgumentNullException.ThrowIfNull(configureSettings);

        AuthLdapSettings.EnableLdapClaimResolution = true;
        configureSettings(AuthLdapSettings);
    }

    /// <summary>
    /// Indicates if integrated server Windows Auth is being used instead of this handler.
    /// See <see cref="PostConfigureAuthNegotiateOptions"/>.
    /// </summary>
    internal bool DeferToServer { get; set; }
}

public class AuthAuthenticatedContext : ResultContext<AuthNegotiateOptions>
{
    /// <summary>
    /// Creates a new <see cref="AuthenticatedContext"/>.
    /// </summary>
    /// <inheritdoc />
    public AuthAuthenticatedContext(
        HttpContext context,
        AuthenticationScheme scheme,
        AuthNegotiateOptions options)
        : base(context, scheme, options) { }
}

public class AuthLdapContext : ResultContext<AuthNegotiateOptions>
{
    /// <summary>
    /// Creates a new <see cref="LdapContext"/>.
    /// </summary>
    /// <inheritdoc />
    public AuthLdapContext(
        HttpContext context,
        AuthenticationScheme scheme,
        AuthNegotiateOptions options,
        AuthLdapSettings settings)
        : base(context, scheme, options)
    {
        AuthLdapSettings = settings;
    }

    /// <summary>
    /// The LDAP settings to use for the RetrieveLdapClaims event.
    /// </summary>
    public AuthLdapSettings AuthLdapSettings { get; }
}

public class AuthChallengeContext : PropertiesContext<AuthNegotiateOptions>
{
    /// <summary>
    /// Creates a new <see cref="AuthChallengeContext"/>.
    /// </summary>
    /// <inheritdoc />
    public AuthChallengeContext(
        HttpContext context,
        AuthenticationScheme scheme,
        AuthNegotiateOptions options,
        AuthenticationProperties properties)
        : base(context, scheme, options, properties) { }

    /// <summary>
    /// Gets a value that determines if this challenge was handled.
    /// If <see langword="true"/>, will skip any default logic for this challenge.
    /// </summary>
    public bool Handled { get; private set; }

    /// <summary>
    /// Skips any default logic for this challenge.
    /// </summary>
    public void HandleResponse() => Handled = true;
}

public class AuthAuthenticationFailedContext : RemoteAuthenticationContext<AuthNegotiateOptions>
{
    /// <summary>
    /// Creates a <see cref="AuthAuthenticationFailedContext"/>.
    /// </summary>
    /// <inheritdoc />
    public AuthAuthenticationFailedContext(
        HttpContext context,
        AuthenticationScheme scheme,
        AuthNegotiateOptions options)
        : base(context, scheme, options, properties: null) { }

    /// <summary>
    /// The exception that occurred while processing the authentication.
    /// </summary>
    public Exception Exception { get; set; } = default!;
}

public class AuthNegotiateEvents
{
    /// <summary>
    /// Invoked if exceptions are thrown during request processing. The exceptions will be re-thrown after this event unless suppressed.
    /// </summary>
    public Func<AuthAuthenticationFailedContext, Task> OnAuthenticationFailed { get; set; } = context => Task.CompletedTask;

    /// <summary>
    /// Invoked after the authentication before ClaimsIdentity is populated with claims retrieved through the LDAP connection.
    /// This event is invoked when <see cref="AuthLdapSettings.EnableLdapClaimResolution"/> is set to true on <see cref="AuthLdapSettings"/>.
    /// </summary>
    public Func<AuthLdapContext, Task> OnRetrieveLdapClaims { get; set; } = context => Task.CompletedTask;

    /// <summary>
    /// Invoked after the authentication is complete and a ClaimsIdentity has been generated.
    /// </summary>
    public Func<AuthAuthenticatedContext, Task> OnAuthenticated { get; set; } = context => Task.CompletedTask;

    /// <summary>
    /// Invoked before a challenge is sent back to the caller.
    /// </summary>
    public Func<AuthChallengeContext, Task> OnChallenge { get; set; } = context => Task.CompletedTask;

    /// <summary>
    /// Invoked if exceptions are thrown during request processing. The exceptions will be re-thrown after this event unless suppressed.
    /// </summary>
    public virtual Task AuthenticationFailed(AuthAuthenticationFailedContext context) => OnAuthenticationFailed(context);

    /// <summary>
    /// Invoked after the authentication before ClaimsIdentity is populated with claims retrieved through the LDAP connection.
    /// </summary>
    public virtual Task RetrieveLdapClaims(AuthLdapContext context) => OnRetrieveLdapClaims(context);

    /// <summary>
    /// Invoked after the authentication is complete and a ClaimsIdentity has been generated.
    /// </summary>
    public virtual Task Authenticated(AuthAuthenticatedContext context) => OnAuthenticated(context);

    /// <summary>
    /// Invoked before a challenge is sent back to the caller.
    /// </summary>
    public virtual Task Challenge(AuthChallengeContext context) => OnChallenge(context);
}


public class AuthLdapSettings
{
    /// <summary>
    /// Configure whether LDAP connection should be used to resolve claims.
    /// This is mainly used on Linux.
    /// </summary>
    public bool EnableLdapClaimResolution { get; set; }

    /// <summary>
    /// The domain to use for the LDAP connection. This is a mandatory setting.
    /// </summary>
    /// <example>
    /// DOMAIN.com
    /// </example>
    public string Domain { get; set; } = default!;

    /// <summary>
    /// The machine account name to use when opening the LDAP connection.
    /// If this is not provided, the machine wide credentials of the
    /// domain joined machine will be used.
    /// </summary>
    public string? MachineAccountName { get; set; }

    /// <summary>
    /// The machine account password to use when opening the LDAP connection.
    /// This must be provided if a <see cref="MachineAccountName"/> is provided.
    /// </summary>
    public string? MachineAccountPassword { get; set; }

    /// <summary>
    /// This option indicates whether nested groups should be ignored when
    /// resolving Roles. The default is false.
    /// </summary>
    public bool IgnoreNestedGroups { get; set; }

    /// <summary>
    /// The <see cref="LdapConnection"/> to be used to retrieve role claims.
    /// If no explicit connection is provided, an LDAP connection will be
    /// automatically created based on the <see cref="Domain"/>,
    /// <see cref="MachineAccountName"/> and <see cref="MachineAccountPassword"/>
    /// options. If provided, this connection will be used and the
    /// <see cref="Domain"/>, <see cref="MachineAccountName"/> and
    /// <see cref="MachineAccountPassword"/>  options will not be used to create
    /// the <see cref="LdapConnection"/>.
    /// </summary>
    public LdapConnection? LdapConnection { get; set; }

    /// <summary>
    /// The sliding expiration that should be used for entries in the cache for user claims, defaults to 10 minutes.
    /// This is a sliding expiration that will extend each time claims for a user is retrieved.
    /// </summary>
    public TimeSpan ClaimsCacheSlidingExpiration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The absolute expiration that should be used for entries in the cache for user claims, defaults to 60 minutes.
    /// This is an absolute expiration that starts when a claims for a user is retrieved for the first time.
    /// </summary>
    public TimeSpan ClaimsCacheAbsoluteExpiration { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>
    /// The maximum size of the claim results cache, defaults to 100 MB.
    /// </summary>
    public int ClaimsCacheSize { get; set; } = 100 * 1024 * 1024;

    internal MemoryCache? ClaimsCache { get; set; }

    /// <summary>
    /// Validates the <see cref="AuthLdapSettings"/>.
    /// </summary>
    public void Validate()
    {
        if (EnableLdapClaimResolution)
        {
            if (string.IsNullOrEmpty(Domain))
            {
                throw new ArgumentException($"{nameof(EnableLdapClaimResolution)} is set to true but {nameof(Domain)} is not set.");
            }

            if (string.IsNullOrEmpty(MachineAccountName) && !string.IsNullOrEmpty(MachineAccountPassword))
            {
                throw new ArgumentException($"{nameof(MachineAccountPassword)} should only be specified when {nameof(MachineAccountName)} is configured.");
            }
        }
    }
}

static class AuthLdapAdapter
{
    static Regex DistinguishedNameSeparatorRegex = new Regex(@"(?<![^\\]\\),", RegexOptions.Compiled);

    public static async Task RetrieveClaimsAsync(AuthLdapSettings settings, ClaimsIdentity identity, ILogger logger)
    {
        var user = identity.Name!;
        var userAccountNameIndex = user.IndexOf('@');
        var userAccountName = userAccountNameIndex == -1 ? user : user.Substring(0, userAccountNameIndex);

        if (settings.ClaimsCache == null)
        {
            settings.ClaimsCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = settings.ClaimsCacheSize });
        }

        if (settings.ClaimsCache.TryGetValue<IEnumerable<string>>(user, out var cachedClaims) && cachedClaims is not null)
        {
            foreach (var claim in cachedClaims)
            {
                identity.AddClaim(new Claim(identity.RoleClaimType, claim));
            }

            return;
        }

        var distinguishedName = settings.Domain.Split('.').Select(name => $"dc={name}").Aggregate((a, b) => $"{a},{b}");
        var retrievedClaims = new List<string>();

        var filter = $"(&(objectClass=user)(sAMAccountName={userAccountName}))"; // This is using ldap search query language, it is looking on the server for someUser
        var searchRequest = new SearchRequest(distinguishedName, filter, SearchScope.Subtree);

        Debug.Assert(settings.LdapConnection != null);
        var searchResponse = (SearchResponse)await Task<DirectoryResponse>.Factory.FromAsync(
            settings.LdapConnection.BeginSendRequest!,
            settings.LdapConnection.EndSendRequest,
            searchRequest,
            PartialResultProcessing.NoPartialResultSupport,
            null);

        if (searchResponse.Entries.Count > 0)
        {
            if (searchResponse.Entries.Count > 1 && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning($"More than one response received for query: {filter} with distinguished name: {distinguishedName}");
            }

            var userFound = searchResponse.Entries[0]; //Get the object that was found on ldap
            var memberof = userFound.Attributes["memberof"]; // You can access ldap Attributes with Attributes property

            foreach (var group in memberof)
            {
                // Example distinguished name: CN=TestGroup,DC=KERB,DC=local
                var groupDN = $"{Encoding.UTF8.GetString((byte[])group)}";
                var groupCN = DistinguishedNameSeparatorRegex.Split(groupDN)[0].Substring("CN=".Length);

                if (!settings.IgnoreNestedGroups)
                {
                    GetNestedGroups(settings.LdapConnection, identity, distinguishedName, groupCN, logger, retrievedClaims, new HashSet<string>());
                }
                else
                {
                    retrievedClaims.Add(groupCN);
                }
            }

            var entrySize = user.Length * 2; //Approximate the size of stored key in memory cache.
            foreach (var claim in retrievedClaims)
            {
                identity.AddClaim(new Claim(identity.RoleClaimType, claim));
                entrySize += claim.Length * 2; //Approximate the size of stored value in memory cache.
            }

            settings.ClaimsCache.Set(user,
                retrievedClaims,
                new MemoryCacheEntryOptions()
                    .SetSize(entrySize)
                    .SetSlidingExpiration(settings.ClaimsCacheSlidingExpiration)
                    .SetAbsoluteExpiration(settings.ClaimsCacheAbsoluteExpiration));
        }
        else if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning($"No response received for query: {filter} with distinguished name: {distinguishedName}");
        }
    }

    private static void GetNestedGroups(LdapConnection connection, ClaimsIdentity principal, string distinguishedName, string groupCN, ILogger logger, IList<string> retrievedClaims, HashSet<string> processedGroups)
    {
        retrievedClaims.Add(groupCN);

        var filter = $"(&(objectClass=group)(sAMAccountName={groupCN}))"; // This is using ldap search query language, it is looking on the server for someUser
        var searchRequest = new SearchRequest(distinguishedName, filter, SearchScope.Subtree);
        var searchResponse = (SearchResponse)connection.SendRequest(searchRequest);

        if (searchResponse.Entries.Count > 0)
        {
            if (searchResponse.Entries.Count > 1 && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning($"More than one response received for query: {filter} with distinguished name: {distinguishedName}");
            }

            var group = searchResponse.Entries[0]; // Get the object that was found on ldap
            var groupDN = group.DistinguishedName;

            processedGroups.Add(groupDN);

            var memberof = group.Attributes["memberof"]; // You can access ldap Attributes with Attributes property
            if (memberof != null)
            {
                foreach (var member in memberof)
                {
                    var nestedGroupDN = $"{Encoding.UTF8.GetString((byte[])member)}";
                    var nestedGroupCN = DistinguishedNameSeparatorRegex.Split(nestedGroupDN)[0].Substring("CN=".Length);

                    if (processedGroups.Contains(nestedGroupDN))
                    {
                        // We need to keep track of already processed groups because circular references are possible with AD groups
                        return;
                    }

                    GetNestedGroups(connection, principal, distinguishedName, nestedGroupCN, logger, retrievedClaims, processedGroups);
                }
            }
        }
    }
}

internal sealed class AuthNegotiateState : INegotiateState
{
    private static readonly NegotiateAuthenticationServerOptions _serverOptions = new();
    private readonly NegotiateAuthentication _instance;

    public AuthNegotiateState()
    {
        _instance = new NegotiateAuthentication(_serverOptions);
    }

    public string? GetOutgoingBlob(string incomingBlob, out BlobErrorType status, out Exception? error)
    {
        var outgoingBlob = _instance.GetOutgoingBlob(incomingBlob, out var authStatus);

        if (authStatus == NegotiateAuthenticationStatusCode.Completed ||
            authStatus == NegotiateAuthenticationStatusCode.ContinueNeeded)
        {
            status = BlobErrorType.None;
            error = null;
        }
        else
        {
            error = new AuthenticationFailureException(authStatus.ToString());
            if (IsCredentialError(authStatus))
            {
                status = BlobErrorType.CredentialError;
            }
            else if (IsClientError(authStatus))
            {
                status = BlobErrorType.ClientError;
            }
            else
            {
                status = BlobErrorType.Other;
            }
        }

        return outgoingBlob;
    }

    public bool IsCompleted
    {
        get => _instance.IsAuthenticated;
    }

    public string Protocol
    {
        get => _instance.Package;
    }

    public IIdentity GetIdentity()
    {
        var remoteIdentity = _instance.RemoteIdentity;
        return remoteIdentity is ClaimsIdentity claimsIdentity ? claimsIdentity.Clone() : remoteIdentity;
    }

    public void Dispose()
    {
        _instance.Dispose();
    }

    private static bool IsCredentialError(NegotiateAuthenticationStatusCode error)
    {
        return error == NegotiateAuthenticationStatusCode.UnknownCredentials ||
            error == NegotiateAuthenticationStatusCode.CredentialsExpired ||
            error == NegotiateAuthenticationStatusCode.BadBinding;
    }

    private static bool IsClientError(NegotiateAuthenticationStatusCode error)
    {
        return error == NegotiateAuthenticationStatusCode.InvalidToken ||
            error == NegotiateAuthenticationStatusCode.QopNotSupported ||
            error == NegotiateAuthenticationStatusCode.UnknownCredentials ||
            error == NegotiateAuthenticationStatusCode.MessageAltered ||
            error == NegotiateAuthenticationStatusCode.OutOfSequence ||
            error == NegotiateAuthenticationStatusCode.InvalidCredentials;
    }
}