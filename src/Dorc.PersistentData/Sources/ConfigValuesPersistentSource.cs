using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;

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

        public string? GetConfigValue(string key, string? defaultValue = null)
        {
            using var context = _contextFactory.GetContext();
            var configValue = context.ConfigValues.SingleOrDefault(x => x.Key == key);

            if (configValue != null)
            {
                return configValue.Secure ? _propertyEncrypt.DecryptValue(configValue.Value ?? string.Empty) : configValue.Value;
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
                    configValue.Value = _propertyEncrypt.DecryptValue(configValue.Value ?? string.Empty);
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

        /// Only one property changes per request (Secure OR IsForProd OR Value).
        public ConfigValueApiModel UpdateConfigValue(ConfigValueApiModel model)
        {
            using var context = _contextFactory.GetContext();
            var configValue = context.ConfigValues
                .FirstOrDefault(pv => pv.Id == model.Id)
                ?? throw new KeyNotFoundException($"Config value with Id={model.Id} not found.");

            // Get current value in plain text (decrypt if Secure) for comparison and restoring when Secure is toggled off
            string oldPlain = configValue.Secure && !string.IsNullOrEmpty(configValue.Value)
                ? _propertyEncrypt.DecryptValue(configValue.Value)
                  ?? throw new ApplicationException($"Failed to decrypt secure value for Id={configValue.Id}")
                : configValue.Value ?? string.Empty;

            // 1) If Secure changed encrypt/decrypt value
            if (configValue.Secure != model.Secure)
            {
                configValue.Secure = model.Secure;
                if (model.Secure)
                {
                    if (model.Value == null)
                        throw new ArgumentException("Value required when Secure=true.");
                    configValue.Value = _propertyEncrypt.EncryptValue(model.Value);
                }

                else
                {
                    configValue.Value = oldPlain;
                }
            }

            // 2) Else if IsForProd changed update the flag
            else if (configValue.IsForProd.GetValueOrDefault() != model.IsForProd.GetValueOrDefault())
            {
                configValue.IsForProd = model.IsForProd;
            }

            // 3) Else if Value changed update (encrypt if Secure)
            else if (model.Value != null && !string.Equals(model.Value, oldPlain, StringComparison.Ordinal))
            {
                configValue.Value = configValue.Secure
                    ? _propertyEncrypt.EncryptValue(model.Value)
                    : model.Value;
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
