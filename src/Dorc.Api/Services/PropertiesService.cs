using System.Security.Claims;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
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

        public PropertiesService(
            IPropertiesPersistentSource propertiesPersistentSource,
            IPropertyValuesService propertyValuesService,
            IClaimsPrincipalReader claimsPrincipalReader,
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IPropertyEncryptor propertyEncryptor,
            ILogger<PropertiesService> logger
            )
        {
            _propertyValuesService = propertyValuesService;
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
                _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod()?.Name} with argument: {propertyName} failed: {e.Message}");
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
                _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod()?.Name} failed: {e.Message}");
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
                    _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod()?.Name} failed: {e.Message}");
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
                    _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod()?.Name} failed: {e.Message}");
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
                    _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod()?.Name} failed: {e.Message}");
                    result.Add(UnrollException(e, propertyUpdateEntry));
                }
            }

            return result;
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