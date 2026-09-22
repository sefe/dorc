namespace Dorc.Api.Interfaces
{
    /// <summary>
    /// Acquires Azure AD bearer tokens using the client credentials flow.
    /// Shared by ServiceNow and Email services.
    /// </summary>
    public interface IAzureAdTokenService
    {
        /// <summary>Acquires a token for the given scope. Returns null on failure.</summary>
        Task<string?> GetTokenAsync(string scope);
    }
}
