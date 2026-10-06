namespace Dorc.PersistentData.Sources
{
    /// <summary>
    /// Thrown when a Secure configuration value is requested through the non-secure accessor
    /// (<see cref="IConfigValuesPersistentSource.GetNonSecureConfigValue"/>). Secret values must
    /// never be exposed through callers that surface config outside the server.
    /// </summary>
    public class SecureConfigValueRequestedException : InvalidOperationException
    {
        public SecureConfigValueRequestedException(string key)
            : base($"Configuration value '{key}' is secure and cannot be retrieved through this endpoint.")
        {
        }
    }
}
