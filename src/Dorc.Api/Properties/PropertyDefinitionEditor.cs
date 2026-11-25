using System.Linq;
using System.Security.Claims;
using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Properties
{
    public class PropertyDefinitionEditor : IPropertyDefinitionEditor
    {
        private readonly ILogger _log;
        private readonly IPropertiesPersistentSource _propertiesPersistentSource;
        private readonly IScopedPropertyValueEditor _scopedPropertyValueEditor;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly IPropertyEncryptor _propertyEncryptor;

        public PropertyDefinitionEditor(
            IPropertiesPersistentSource propertiesPersistentSource,
            IScopedPropertyValueEditor scopedPropertyValueEditor,
            IClaimsPrincipalReader claimsPrincipalReader,
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IPropertyEncryptor propertyEncryptor,
            ILogger<PropertyDefinitionEditor> logger
            )
        {
            _scopedPropertyValueEditor = scopedPropertyValueEditor;
            _propertiesPersistentSource = propertiesPersistentSource;
            _log = logger;
            _claimsPrincipalReader = claimsPrincipalReader;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _propertyEncryptor = propertyEncryptor;
        }

        public PropertyApiModel GetProperty(string propertyName)
        {
            try
            {
                return _propertiesPersistentSource.GetProperty(propertyName);
            }
            catch (Exception e)
            {
                _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod().Name} with argument: {propertyName} failed: {e.Message}");
                return null;
            }
        }

        public IEnumerable<PropertyApiModel> GetProperties()
        {
            try
            {
                return _propertiesPersistentSource.GetAllProperties();
            }
            catch (Exception e)
            {
                _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod().Name} failed: {e.Message}");
                return new List<PropertyApiModel>();
            }
        }

        public IEnumerable<Response> DeleteProperties(IEnumerable<string> properties, ClaimsPrincipal User)
        {
            var result = new List<Response>();
            foreach (var property in properties)
            {
                try
                {
                    var propValues = _scopedPropertyValueEditor.GetPropertyValues(property, null, User);
                    result.AddRange(_scopedPropertyValueEditor.DeletePropertyValues(propValues, User));

                    string username = _claimsPrincipalReader.GetUserFullDomainName(User);
                    if (_propertiesPersistentSource.DeleteProperty(property, username))
                    {
                        result.Add(new Response { Item = property, Status = "success" });
                        continue;
                    }
                    result.Add(new Response { Item = property, Status = "error: failed deletion from the database" });

                }
                catch (Exception e)
                {
                    _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod().Name} failed: {e.Message}");
                    result.Add(UnrollException(e, property));
                }
            }

            return result;
        }

        private static Response UnrollException(Exception e, object property)
        {
            return e.Message.Contains("inner exception for details")
                ? UnrollException(e.InnerException, property)
                : new Response { Item = property, Status = e.Message };
        }

        public IEnumerable<Response> PostProperties(IEnumerable<PropertyApiModel> properties, ClaimsPrincipal User)
        {
            var result = new List<Response>();

            foreach (var property in properties)
            {
                if (string.IsNullOrEmpty(property.Name.Trim()))
                {
                    result.Add(new Response { Item = property, Status = "error : Please specify a Variable Name" });
                    return result;
                }

                try
                {
                    string username = _claimsPrincipalReader.GetUserFullDomainName(User);
                    _propertiesPersistentSource.CreateProperty(property, username);
                    result.Add(new Response { Item = property, Status = "success" });
                }
                catch (Exception e)
                {
                    _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod().Name} failed: {e.Message}");
                    result.Add(UnrollException(e, property));
                }
            }

            return result;
        }

        public IEnumerable<Response> PutProperties(IDictionary<string, PropertyApiModel> propertiesToUpdate,
            ClaimsPrincipal User)
        {
            var result = new List<Response>();

            foreach (var propertyUpdateEntry in propertiesToUpdate)
            {
                try
                {
                    var lookupName = !string.IsNullOrWhiteSpace(propertyUpdateEntry.Key)
                        ? propertyUpdateEntry.Key
                        : propertyUpdateEntry.Value.Name;
                    if (string.IsNullOrWhiteSpace(propertyUpdateEntry.Value.Name))
                    {
                        propertyUpdateEntry.Value.Name = lookupName;
                    }

                    // Get the current property state before update
                    var currentProperty = _propertiesPersistentSource.GetProperty(lookupName)
                                          ?? _propertiesPersistentSource.GetProperty(propertyUpdateEntry.Value.Name);
                    if (currentProperty == null)
                    {
                        result.Add(new Response
                        { Item = propertyUpdateEntry, Status = "error: variable does not exist - please use POST to create it" });
                        continue;
                    }

                    propertyUpdateEntry.Value.Id = currentProperty.Id;

                    // Check if property is being changed from non-secure to secure
                    bool shouldEncryptExistingValues = !currentProperty.Secure && propertyUpdateEntry.Value.Secure;
                    if (shouldEncryptExistingValues)
                    {
                        var encryptionResult = EncryptExistingPropertyValues(
                            propertyUpdateEntry.Value.Name,
                            propertyUpdateEntry.Value.IsArray,
                            () => _propertiesPersistentSource.UpdateProperty(propertyUpdateEntry.Value));
                        if (encryptionResult != null)
                        {
                            result.Add(encryptionResult);
                            continue;
                        }

                        result.Add(new Response { Item = propertyUpdateEntry, Status = "success" });
                        continue;
                    }

                    if (_propertiesPersistentSource.UpdateProperty(propertyUpdateEntry.Value))
                    {
                        result.Add(new Response { Item = propertyUpdateEntry, Status = "success" });
                        continue;
                    }

                    result.Add(new Response
                    { Item = propertyUpdateEntry, Status = "error: failed to update variable in the database" });
                }
                catch (NullReferenceException)
                {
                    result.Add(new Response
                    { Item = propertyUpdateEntry, Status = "error: variable does not exist - please use POST to create it" });
                }
                catch (Exception e)
                {
                    _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod().Name} failed: {e.Message}");
                    result.Add(UnrollException(e, propertyUpdateEntry));
                }
            }

            return result;
        }

        public IEnumerable<Response> ImportProperties(IEnumerable<PropertyImportCsvDto> propertiesToImport,
            ClaimsPrincipal User)
        {
            var result = new List<Response>();
            var importRows = propertiesToImport == null ? new List<PropertyImportCsvDto>() : propertiesToImport.ToList();

            if (!importRows.Any())
            {
                return result;
            }

            foreach (var row in importRows)
            {
                if (string.IsNullOrWhiteSpace(row.PropertyName))
                {
                    result.Add(new Response
                    {
                        Item = row,
                        Status = "error: PropertyName cannot be blank or empty"
                    });
                }

                if (string.IsNullOrWhiteSpace(row.Value))
                {
                    result.Add(new Response
                    {
                        Item = row,
                        Status = "error: Property value cannot be blank or empty"
                    });
                }
            }

            if (result.Any())
            {
                return result;
            }

            var propertySecurityMap = new Dictionary<string, bool>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var row in importRows)
            {
                if (!propertySecurityMap.TryAdd(row.PropertyName, row.IsSecure)
                    && propertySecurityMap[row.PropertyName] != row.IsSecure)
                {
                    result.Add(new Response
                    {
                        Item = row,
                        Status = $"error: conflicting IsSecure values for property '{row.PropertyName}'"
                    });
                }
            }

            if (result.Any())
            {
                return result;
            }

            var properties = propertySecurityMap
                .Select(kvp => new PropertyApiModel
                {
                    Name = kvp.Key,
                    Secure = kvp.Value
                })
                .ToArray();

            var propertyValues = importRows
                .Select(row => new PropertyValueDto
                {
                    Property = new PropertyApiModel
                    {
                        Name = row.PropertyName,
                        Secure = row.IsSecure
                    },
                    Value = row.Value,
                    PropertyValueFilter = string.IsNullOrWhiteSpace(row.Environment)
                        ? null
                        : row.Environment
                })
                .ToArray();

            result.AddRange(PostProperties(properties, User));
            result.AddRange(_scopedPropertyValueEditor.PostPropertyValues(propertyValues, User));
            result.AddRange(_scopedPropertyValueEditor.PutPropertyValues(propertyValues, User));

            return result;
        }

        private Response? EncryptExistingPropertyValues(string propertyName, bool isArray, Func<bool> commitPropertyUpdate)
        {
            try
            {
                var propertyValues = _propertyValuesPersistentSource.GetPropertyValuesByName(propertyName);
                var encryptedUpdates = new List<(long Id, string EncryptedValue)>();

                if (propertyValues != null)
                {
                    foreach (var propertyValue in propertyValues.Where(pv => !string.IsNullOrEmpty(pv.Value)))
                    {
                        var encryptedValue = isArray
                            ? Dorc.PersistentData.Sources.PropertyValuesPersistentSource.EncryptArrayValue(propertyValue.Value!, _propertyEncryptor)
                            : _propertyEncryptor.EncryptValue(propertyValue.Value!);
                        encryptedUpdates.Add((propertyValue.Id, encryptedValue));
                    }
                }

                if (!commitPropertyUpdate())
                {
                    return new Response
                    {
                        Item = propertyName,
                        Status = "error: failed to update variable in the database"
                    };
                }

                foreach (var (id, encryptedValue) in encryptedUpdates)
                {
                    _propertyValuesPersistentSource.UpdatePropertyValue(id, encryptedValue);
                    _log.LogInformation($"Encrypted property value for property '{propertyName}' (ID: {id})");
                }

                return null;
            }
            catch (Exception e)
            {
                _log.LogError(e, $"Failed to encrypt existing property values for property '{propertyName}': {e.Message}");
                return new Response 
                { 
                    Item = propertyName, 
                    Status = $"error: failed to encrypt existing property values - {e.Message}" 
                };
            }
        }
    }
}