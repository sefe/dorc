using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.PersistentData.Sources
{
    public class ConfigValuesPersistentSource : IConfigValuesPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly IPropertyEncryptor _propertyEncrypt;

        public ConfigValuesPersistentSource(IDeploymentContextFactory contextFactory,
            IPropertyEncryptor propertyEncrypt)
        {
            _propertyEncrypt = propertyEncrypt;
            _contextFactory = contextFactory;
        }

        public string GetConfigValue(string key, string defaultValue = null)
        {
            using var context = _contextFactory.GetContext();
            var configValue = context.ConfigValues.SingleOrDefault(x => x.Key == key);

            if (configValue != null)
            {
                return configValue.Secure ? _propertyEncrypt.DecryptValue(configValue.Value) : configValue.Value;
            }

            return defaultValue;
        }

        public IEnumerable<ConfigValueApiModel> GetAllConfigValues(bool decryptSecure)
        {
            using var context = _contextFactory.GetContext();
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

        public bool RemoveConfigValue(int configValueId)
        {
            using var context = _contextFactory.GetContext();
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

        public ConfigValueApiModel UpdateConfigValue(ConfigValueApiModel model)
        {
            using var context = _contextFactory.GetContext();
            var configValue = context.ConfigValues
                .FirstOrDefault(pv => pv.Id == model.Id)
                ?? throw new KeyNotFoundException($"Config value with Id={model.Id} not found.");

            bool oldSecure = configValue.Secure;
            bool oldIsForProd = configValue.IsForProd.GetValueOrDefault();
            string oldValue = configValue.Value ?? string.Empty;

            configValue.Secure = model.Secure;
            configValue.Key = model.Key;
            configValue.IsForProd = model.IsForProd;

            bool secureChanged = oldSecure != model.Secure;
            bool isForProdChanged = oldIsForProd != model.IsForProd.GetValueOrDefault();

            string ResolveExistingPlain()
            {
                if (string.IsNullOrEmpty(oldValue)) return string.Empty;
                if (!oldSecure) return oldValue;

                var plain = _propertyEncrypt.DecryptValue(oldValue);
                if (plain == null)
                    throw new ApplicationException($"Failed to decrypt secure value for Id={configValue.Id}");
                return plain;
            }

            string existingPlain = ResolveExistingPlain();

            if (secureChanged)
            {
                if (model.Secure)
                {
                    if (model.Value == null)
                        throw new ArgumentException("When toggling Secure to true, model.Value must contain plaintext.");
                    configValue.Value = _propertyEncrypt.EncryptValue(model.Value);
                }
                else
                {
                    configValue.Value = existingPlain;
                }
            }

            else if (model.Value != null && !isForProdChanged)
            {
                bool sameAsStored = string.Equals(model.Value, configValue.Value, StringComparison.Ordinal);
                bool sameAsPlain = string.Equals(model.Value, existingPlain, StringComparison.Ordinal);

                if (!sameAsStored && !sameAsPlain)
                {
                    configValue.Value = configValue.Secure
                        ? _propertyEncrypt.EncryptValue(model.Value)
                        : model.Value;
                }
            }

            context.SaveChanges();
            return MapToConfigValueApiModel(configValue);
        }

        public ConfigValueApiModel? Add(ConfigValueApiModel model)
        {
            using var context = _contextFactory.GetContext();

            var existingConfigValue = context.ConfigValues
                .FirstOrDefault(pv =>
                    pv.Key == model.Key &&
                    (pv.IsForProd ?? false) == (model.IsForProd ?? false));

            if (existingConfigValue != null)
            {
                throw new ArgumentException($"A config value with the key '{model.Key}' and IsForProd = {(model.IsForProd ?? false)} already exists");
            }

            var configValue = new ConfigValue
            {
                Key = model.Key,
                Value = model.Value,
                Secure = model.Secure,
                IsForProd = model.IsForProd
            };

            if (model.Secure)
            {
                configValue.Value = _propertyEncrypt.EncryptValue(configValue.Value);
            }

            context.ConfigValues.Add(configValue);
            context.SaveChanges();
            return MapToConfigValueApiModel(configValue);
        }

        private static ConfigValueApiModel MapToConfigValueApiModel(ConfigValue configValue)
        {
            return new ConfigValueApiModel
            {
                Id = configValue.Id,
                Key = configValue.Key,
                Value = configValue.Value,
                Secure = configValue.Secure,
                IsForProd = configValue.IsForProd
            };
        }
    }
}
