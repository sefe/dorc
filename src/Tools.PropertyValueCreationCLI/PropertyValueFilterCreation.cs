using System.Linq;
using System.Security.Principal;
using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

namespace Tools.PropertyValueCreationCLI
{
    internal class PropertyValueFilterCreation : IPropertyValueFilterCreation
    {
        private readonly ILogger _log;
        private readonly IPropertyEncryptor _encryptor;
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly IPropertyValuesAuditPersistentSource _propertyValuesAuditPersistentSource;

        public PropertyValueFilterCreation(ILogger log, IPropertyEncryptor propertyEncryptor, IPropertyValuesPersistentSource propertyValuesPersistentSource, IPropertyValuesAuditPersistentSource propertyValuesAuditPersistentSource)
        {
            _propertyValuesAuditPersistentSource = propertyValuesAuditPersistentSource;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _encryptor = propertyEncryptor;
            _log = log;
        }

        public void InsertPropertyValueFilter(string name, string value, string envName)
        {
            var existingPropertyValues = _propertyValuesPersistentSource.GetPropertyValues(name, value, true)
                .FirstOrDefault()?.Value;
            if (string.IsNullOrEmpty(existingPropertyValues))
            {
                if (string.IsNullOrEmpty(envName))
                    _log.LogWarning("Default property already configured for property " + name);
                else
                    _log.LogWarning("Property Value already configured for environment '" + envName + "'");
            }
            else
            {
                var prop = new PropertyApiModel
                {
                    Name = name,
                };
                var newValue = _propertyValuesPersistentSource.AddPropertyValue(new PropertyValueDto
                {
                    PropertyValueFilter = envName,
                    Property = prop,
                    Value = value
                });
                
                _propertyValuesAuditPersistentSource.AddRecord(newValue.Property.Id, newValue.Id, newValue.Property.Name, envName,
                    "", newValue.Value, WindowsIdentity.GetCurrent().Name, "Insert");
                
            }
        }
    }
}