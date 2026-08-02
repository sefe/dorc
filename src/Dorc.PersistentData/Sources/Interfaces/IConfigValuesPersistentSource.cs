using Dorc.ApiModel;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces;

public interface IConfigValuesPersistentSource
{
    string GetConfigValue(string key, string defaultValue = null);

    /// <summary>
    /// Returns the value of a non-secure config key, or null when the key does not exist
    /// or is marked Secure. Use this for any caller that exposes config values outside the
    /// server (e.g. HTTP endpoints) so that secret values are never disclosed.
    /// </summary>
    string? GetNonSecureConfigValue(string key);

    IEnumerable<ConfigValueApiModel> GetAllConfigValues(bool decryptSecure);
    bool RemoveConfigValue(int configValueId);
    ConfigValueApiModel UpdateConfigValue(ConfigValueApiModel newValue);
    ConfigValueApiModel? Add(ConfigValueApiModel model);
}