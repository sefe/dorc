using Dorc.ApiModel;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces;

public interface IConfigValuesPersistentSource
{
    string GetConfigValue(string key, string defaultValue = null);
    IEnumerable<ConfigValueApiModel> GetAllConfigValues(bool decryptSecure);
    bool RemoveConfigValue(int configValueId);
    ConfigValueApiModel UpdateConfigValue(ConfigValue newValue);
    ConfigValueApiModel? Add(ConfigValueApiModel model);
}