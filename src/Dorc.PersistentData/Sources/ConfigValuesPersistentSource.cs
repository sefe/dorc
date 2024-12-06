using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using System.Reflection.Metadata.Ecma335;

namespace Dorc.PersistentData.Sources
{
    public class ConfigValuesPersistentSource : IConfigValuesPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private IPropertyEncryptor _propertyEncrypt;

        public ConfigValuesPersistentSource(IDeploymentContextFactory contextFactory,
            IPropertyEncryptor propertyEncrypt)
        {
            _propertyEncrypt = propertyEncrypt;
            _contextFactory = contextFactory;
        }

        public string GetConfigValue(string key, string defaultValue = null)
        {
            using (var context = _contextFactory.GetContext())
            {
                var configValue = context.ConfigValues.SingleOrDefault(x => x.Key == key);

                if (configValue != null)
                {
                    return configValue.Secure ? _propertyEncrypt.DecryptValue(configValue.Value) : configValue.Value;
                }

                return defaultValue;
            }
        }

        public IEnumerable<ConfigValueApiModel> GetAllConfigValues(bool decryptSecure)
        {
            using (var context = _contextFactory.GetContext())
            {
                var allConfigValues = context.GetAllConfigValues();

                foreach (var configValue in allConfigValues)
                {
                    if (configValue.Secure && decryptSecure)
                    {
                        configValue.Value = _propertyEncrypt.DecryptValue(configValue.Value);
                    }
                }
                return allConfigValues.Select(MapToConfigValueApiModel);
            }
        }

        public bool RemoveConfigValue(int configValueId)
        {
            using (var context = _contextFactory.GetContext())
            {
                try
                {
                    var remove = context.ConfigValues.FirstOrDefault(cv => cv.Id == configValueId);
                    if (remove == null)
                        return false;
                    context.ConfigValues.Remove(remove);
                    context.SaveChanges();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public ConfigValueApiModel UpdateConfigValue(ConfigValue newValue)
        {
            using (var context = _contextFactory.GetContext())
            {
                var configValue = context.ConfigValues
                    .First(pv => pv.Id == newValue.Id);

                configValue.Value = newValue.Value;
                configValue.Secure = newValue.Secure;
                configValue.Key = newValue.Key;

                if (configValue.Secure)
                {
                    configValue.Value = _propertyEncrypt.EncryptValue(configValue.Value);
                }

                context.SaveChanges();
                return MapToConfigValueApiModel(configValue);
            }
        }

        public ConfigValueApiModel? Add(ConfigValueApiModel model)
        {
            using (var context = _contextFactory.GetContext())
            {
                var configValue = new ConfigValue
                {
                    Key = model.Key,
                    Value = model.Value,
                    Secure = model.Secure
                };

                if (model.Secure)
                {
                    configValue.Value = _propertyEncrypt.EncryptValue(configValue.Value);
                }

                context.ConfigValues.Add(configValue);
                context.SaveChanges();
                return MapToConfigValueApiModel(configValue);
            }
        }

        private static ConfigValueApiModel MapToConfigValueApiModel(ConfigValue configValue)
        {
            return new ConfigValueApiModel
            {
                Id = configValue.Id,
                Key = configValue.Key,
                Value = configValue.Value,
                Secure = configValue.Secure
            };
        }
    }
}
