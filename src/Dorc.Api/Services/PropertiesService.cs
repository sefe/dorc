using System.Security.Claims;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Services
{
    public class PropertiesService : IPropertiesService
    {
        private readonly ILogger _log;
        private readonly IPropertiesPersistentSource _propertiesPersistentSource;
        private readonly IPropertyValuesService _propertyValuesService;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly IPropertyEncryptor _propertyEncryptor;
        private readonly ISecurityPrivilegesChecker? _securityPrivilegesChecker;

        public PropertiesService(
            IPropertiesPersistentSource propertiesPersistentSource,
            IPropertyValuesService propertyValuesService,
            IClaimsPrincipalReader claimsPrincipalReader,
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IPropertyEncryptor propertyEncryptor,
            ILogger<PropertiesService> logger)
            : this(
                propertiesPersistentSource,
                propertyValuesService,
                claimsPrincipalReader,
                propertyValuesPersistentSource,
                propertyEncryptor,
                logger,
                null)
        {
        }

        public PropertiesService(
            IPropertiesPersistentSource propertiesPersistentSource,
            IPropertyValuesService propertyValuesService,
            IClaimsPrincipalReader claimsPrincipalReader,
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IPropertyEncryptor propertyEncryptor,
            ILogger<PropertiesService> logger,
            ISecurityPrivilegesChecker? securityPrivilegesChecker
            )
        {
            _propertyValuesService = propertyValuesService;
            _propertiesPersistentSource = propertiesPersistentSource;
            _log = logger;
            _claimsPrincipalReader = claimsPrincipalReader;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _propertyEncryptor = propertyEncryptor;
            _securityPrivilegesChecker = securityPrivilegesChecker;
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
                    var propValues = _propertyValuesService.GetPropertyValues(property, null, User);
                    result.AddRange(_propertyValuesService.DeletePropertyValues(propValues, User));

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

            return RedactResponsesIfRequired(result, User);
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
                    // Get the current property state before update
                    var currentProperty = _propertiesPersistentSource.GetProperty(propertyUpdateEntry.Value.Name);
                    if (currentProperty == null)
                    {
                        result.Add(new Response
                        { Item = propertyUpdateEntry, Status = "error: variable does not exist - please use POST to create it" });
                        continue;
                    }

                    // Check if property is being changed from non-secure to secure
                    bool shouldEncryptExistingValues = !currentProperty.Secure && propertyUpdateEntry.Value.Secure;

                    if (_propertiesPersistentSource.UpdateProperty(propertyUpdateEntry.Value))
                    {
                        // If property was changed to secure, encrypt all existing property values
                        if (shouldEncryptExistingValues)
                        {
                            var encryptionResult = EncryptExistingPropertyValues(propertyUpdateEntry.Value.Name, User);
                            if (encryptionResult != null)
                            {
                                result.Add(encryptionResult);
                                continue;
                            }
                        }

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
            var importRows = propertiesToImport?.ToList() ?? new List<PropertyImportCsvDto>();

            if (!importRows.Any())
            {
                return result;
            }

            var securePropertyNames = new HashSet<string>(
                importRows
                    .Where(r => r != null && r.IsSecure && !string.IsNullOrWhiteSpace(r.PropertyName))
                    .Select(r => r.PropertyName),
                StringComparer.CurrentCultureIgnoreCase);

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
                return RedactResponsesIfRequired(result, User, securePropertyNames);
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
                return RedactResponsesIfRequired(result, User, securePropertyNames);
            }

            var properties = propertySecurityMap
                .Select(kvp => new PropertyApiModel
                {
                    Name = kvp.Key,
                    Secure = kvp.Value
                })
                .ToArray();

            PropertyValueDto[] CreatePropertyValueDtos() => importRows
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
            result.AddRange(_propertyValuesService.PostPropertyValues(CreatePropertyValueDtos(), User));
            result.AddRange(_propertyValuesService.PutPropertyValues(CreatePropertyValueDtos(), User));

            return RedactResponsesIfRequired(result, User, securePropertyNames);
        }

        private List<Response> RedactResponsesIfRequired(
            IEnumerable<Response> responses,
            ClaimsPrincipal user,
            ISet<string>? batchSecurePropertyNames = null)
        {
            return responses
                .Select(r => new Response
                {
                    Item = RedactResponseItemIfRequired(r.Item, user, batchSecurePropertyNames),
                    Status = r.Status
                })
                .ToList();
        }

        private object? RedactResponseItemIfRequired(
            object? item,
            ClaimsPrincipal user,
            ISet<string>? batchSecurePropertyNames = null)
        {
            switch (item)
            {
                case PropertyValueDto pvDto:
                    if (!IsSecureProperty(pvDto.Property?.Name, pvDto.Property?.Secure, batchSecurePropertyNames) ||
                        CanReadSecrets(user, pvDto.PropertyValueFilter))
                    {
                        return pvDto;
                    }

                    return new PropertyValueDto
                    {
                        Id = pvDto.Id,
                        Value = string.Empty,
                        Property = pvDto.Property == null
                            ? null
                            : new PropertyApiModel
                            {
                                Id = pvDto.Property.Id,
                                Name = pvDto.Property.Name,
                                Secure = pvDto.Property.Secure,
                                IsArray = pvDto.Property.IsArray
                            },
                        PropertyValueFilter = pvDto.PropertyValueFilter,
                        PropertyValueFilterId = pvDto.PropertyValueFilterId,
                        Priority = pvDto.Priority,
                        DefaultValue = pvDto.DefaultValue,
                        UserEditable = pvDto.UserEditable
                    };

                case PropertyImportCsvDto csvDto:
                    if (!IsSecureProperty(csvDto.PropertyName, csvDto.IsSecure, batchSecurePropertyNames) ||
                        CanReadSecrets(user, csvDto.Environment))
                    {
                        return csvDto;
                    }

                    return new PropertyImportCsvDto
                    {
                        PropertyName = csvDto.PropertyName,
                        Value = string.Empty,
                        IsSecure = csvDto.IsSecure,
                        Environment = csvDto.Environment
                    };

                default:
                    return item;
            }
        }

        private bool CanReadSecrets(ClaimsPrincipal user, string? environmentName)
        {
            var normalizedEnvironment = string.IsNullOrWhiteSpace(environmentName)
                ? null
                : environmentName;
            return _securityPrivilegesChecker?.CanReadSecrets(user, normalizedEnvironment) == true;
        }

        private bool IsSecureProperty(
            string? propertyName,
            bool? dtoSecure,
            ISet<string>? batchSecurePropertyNames = null)
        {
            if (dtoSecure == true)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            if (batchSecurePropertyNames != null && batchSecurePropertyNames.Contains(propertyName))
            {
                return true;
            }

            try
            {
                return _propertiesPersistentSource.GetProperty(propertyName)?.Secure == true;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "Failed to resolve security metadata for property '{PropertyName}'; redacting value.",
                    propertyName);
                return true;
            }
        }

        private Response? EncryptExistingPropertyValues(string propertyName, ClaimsPrincipal user)
        {
            try
            {
                // Get all property values for this property
                var propertyValues = _propertyValuesPersistentSource.GetPropertyValuesByName(propertyName);
                
                if (propertyValues == null || !propertyValues.Any())
                {
                    // No property values exist, nothing to encrypt
                    return null;
                }

                // Encrypt each property value
                foreach (var propertyValue in propertyValues)
                {
                    if (!string.IsNullOrEmpty(propertyValue.Value))
                    {
                        var encryptedValue = _propertyEncryptor.EncryptValue(propertyValue.Value);
                        _propertyValuesPersistentSource.UpdatePropertyValue(propertyValue.Id, encryptedValue);
                        
                        _log.LogInformation($"Encrypted property value for property '{propertyName}' (ID: {propertyValue.Id})");
                    }
                }

                return null; // Success, no error response
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