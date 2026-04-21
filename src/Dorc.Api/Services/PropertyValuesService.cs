using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.VisualStudio.Services.Common;
using System.Security.Claims;

namespace Dorc.Api.Services
{
    public class PropertyValuesService : IPropertyValuesService
    {
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IPropertiesPersistentSource _propertiesPersistentSource;
        private readonly IPropertyEncryptor _propertyEncryptor;
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly IPropertyValuesAuditPersistentSource _propertyValuesAuditPersistentSource;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly ILogger _log; 
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public PropertyValuesService(ISecurityPrivilegesChecker securityPrivilegesChecker, IPropertyEncryptor propertyEncryptor,
            IPropertiesPersistentSource propertiesPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IPropertyValuesAuditPersistentSource propertyValuesAuditPersistentSource,
            IRolePrivilegesChecker rolePrivilegesChecker,
            ILogger<PropertyValuesService> log,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _propertyValuesAuditPersistentSource = propertyValuesAuditPersistentSource;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
            _propertiesPersistentSource = propertiesPersistentSource;
            _propertyEncryptor = propertyEncryptor;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _log = log;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        public IEnumerable<PropertyValueDto> GetPropertyValues(string propertyName, string environmentName,
            ClaimsPrincipal user)
        {
            string username = _claimsPrincipalReader.GetUserLogin(user);
            var userSids = _claimsPrincipalReader.GetSidsForUser(user);

            var allSids = string.Join(";", userSids);

            var result = new List<PropertyValueDto>();

            var propValues = _propertyValuesPersistentSource.GetPropertyValuesForUser(environmentName, propertyName, username, allSids).ToList();
            result.AddRange(propValues);
            var canReadSecrets = _securityPrivilegesChecker.CanReadSecrets(user, environmentName);

            if (!canReadSecrets && !String.IsNullOrEmpty(environmentName))
            {
                if (result.Any() && result.All(propertyValueDto => propertyValueDto.Property.Secure))
                    throw new NonEnoughRightsException($"User {username} doesn't have \"ReadSecrets\" permission to read secured properties");

                result.Where(propertyValueDto => propertyValueDto.Property.Secure).ForEach(propertyValueDto =>
                {
                    propertyValueDto.Value = String.Empty;
                });
            }

            if (canReadSecrets)
            {
                foreach (var propertyValueDto in result.Where(propertyValueDto => propertyValueDto.Property.Secure))
                {
                    propertyValueDto.Value = _propertyEncryptor.DecryptValue(propertyValueDto.Value);
                }
            }

            if (_rolePrivilegesChecker.IsAdmin(user))
            {
                result.ForEach(value => value.UserEditable = true);
            }

            return result;
        }

        public IEnumerable<Response> DeletePropertyValues(
            IEnumerable<PropertyValueDto> propertyValuesToDelete, ClaimsPrincipal user)
        {
            var result = new List<Response>();
            foreach (var propertyValueDto in propertyValuesToDelete)
            {
                result.Add(ProcessDeletePropertyValue(propertyValueDto, user));
            }
            return result;
        }

        private Response ProcessDeletePropertyValue(PropertyValueDto propertyValueDto, ClaimsPrincipal user)
        {
            try
            {
                if (!_securityPrivilegesChecker.CanModifyPropertyValue(user, propertyValueDto.PropertyValueFilter))
                {
                    return new Response
                    {
                        Item = propertyValueDto,
                        Status = "error: you don't have permissions to edit variable value(s) for this environment"
                    };
                }

                var filteredPropertyValues = FindMatchingPropertyValue(propertyValueDto);

                if (filteredPropertyValues?.Id == 0)
                {
                    return new Response
                    {
                        Item = propertyValueDto,
                        Status = "error: variable value does not exist"
                    };
                }

                if (!TryRemovePropertyValue(filteredPropertyValues, propertyValueDto.PropertyValueFilter))
                {
                    return new Response
                    {
                        Item = propertyValueDto,
                        Status = "error: deletion from the database failed"
                    };
                }

                AuditPropertyValueChange(filteredPropertyValues, filteredPropertyValues?.Value, string.Empty, "Delete", user);

                return new Response { Item = propertyValueDto, Status = "success" };
            }
            catch (Exception e)
            {
                _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod().Name} with argument: {propertyValueDto.Property.Name} failed: {e.Message}");
                return new Response { Item = propertyValueDto, Status = e.Message };
            }
        }

        private PropertyValueDto? FindMatchingPropertyValue(PropertyValueDto propertyValueDto)
        {
            var propertyValuesByName = _propertyValuesPersistentSource
                .GetPropertyValuesByName(propertyValueDto.Property.Name);

            return string.IsNullOrEmpty(propertyValueDto.PropertyValueFilter)
                ? propertyValuesByName.FirstOrDefault(pv => pv.PropertyValueFilter == null)
                : propertyValuesByName.FirstOrDefault(pv =>
                    pv.PropertyValueFilter != null &&
                    pv.PropertyValueFilter.Equals(propertyValueDto.PropertyValueFilter,
                        StringComparison.CurrentCultureIgnoreCase));
        }

        private bool TryRemovePropertyValue(PropertyValueDto? filteredPropertyValues, string? propertyValueFilter)
        {
            if (propertyValueFilter != null)
            {
                return _propertyValuesPersistentSource.RemoveByFilterId(filteredPropertyValues?.PropertyValueFilterId) ||
                       _propertyValuesPersistentSource.Remove(filteredPropertyValues?.Id);
            }

            return _propertyValuesPersistentSource.Remove(filteredPropertyValues?.Id);
        }

        public IEnumerable<Response> PostPropertyValues(
            IEnumerable<PropertyValueDto> propertyValuesToCreate, ClaimsPrincipal user)
        {
            var result = new List<Response>();

            foreach (var propertyValueDto in propertyValuesToCreate)
                try
                {
                    if (!string.IsNullOrWhiteSpace(propertyValueDto.PropertyValueFilter) &&
                        _environmentsPersistentSource.GetEnvironment(propertyValueDto.PropertyValueFilter, user) == null)
                    {
                        result.Add(new Response
                        {
                            Item = propertyValueDto,
                            Status = $"error: environment '{propertyValueDto.PropertyValueFilter}' does not exist"
                        });
                        continue;
                    }

                    if (!_securityPrivilegesChecker.CanModifyPropertyValue(user, propertyValueDto.PropertyValueFilter))
                    {
                        result.Add(new Response
                        {
                            Item = propertyValueDto,
                            Status = $"error: you don't have permissions to edit variable value(s) for environment '{propertyValueDto.PropertyValueFilter}'"
                        });
                        continue;
                    }

                    PropertyValueDto oldVariableValue;
                    if (!string.IsNullOrEmpty(propertyValueDto.PropertyValueFilter))
                        //checking if environment value
                        oldVariableValue = _propertyValuesPersistentSource
                            .GetPropertyValuesByName(propertyValueDto.Property.Name)
                            .FirstOrDefault(pv => pv.PropertyValueFilter != null &&
                                                  pv.PropertyValueFilter.Equals(propertyValueDto.PropertyValueFilter,
                                                      StringComparison.CurrentCultureIgnoreCase));
                    else
                        //checking if default value
                        oldVariableValue = _propertyValuesPersistentSource
                            .GetPropertyValuesByName(propertyValueDto.Property.Name)
                            .FirstOrDefault(pv => pv.PropertyValueFilterId == null);

                    if (oldVariableValue?.Id != null)
                    {
                        result.Add(new Response
                        {
                            Item = propertyValueDto,
                            Status = "error: variable value for this environment already exists"
                        });
                        continue;
                    }

                    var newVariableValue = _propertyValuesPersistentSource.AddPropertyValue(propertyValueDto);

                    var propertyApiModel = _propertiesPersistentSource.GetProperty(newVariableValue.Property.Name);
                    string username = _claimsPrincipalReader.GetUserFullDomainName(user);
                    _propertyValuesAuditPersistentSource.AddRecord(propertyApiModel.Id,
                        newVariableValue.Id, newVariableValue.Property.Name,
                        newVariableValue.PropertyValueFilter, string.Empty, newVariableValue.Value,
                        username, "Insert");

                    result.Add(new Response { Item = propertyValueDto, Status = "success" });
                }
                catch (Exception e)
                {
                    _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod().Name} with argument: {propertyValueDto.Property.Name} failed: {e.Message}");
                    result.Add(new Response { Item = propertyValueDto, Status = e.Message });
                }

            return result;
        }

        public IEnumerable<Response> PutPropertyValues(
            IEnumerable<PropertyValueDto> propertyValuesToUpdate, ClaimsPrincipal user)
        {
            var result = new List<Response>();

            foreach (var propertyValueDto in propertyValuesToUpdate)
            {
                result.Add(ProcessPutPropertyValue(propertyValueDto, user));
            }

            return result;
        }

        private Response ProcessPutPropertyValue(PropertyValueDto propertyValueDto, ClaimsPrincipal user)
        {
            try
            {
                if (!_securityPrivilegesChecker.CanModifyPropertyValue(user, propertyValueDto.PropertyValueFilter))
                {
                    return new Response
                    {
                        Item = propertyValueDto,
                        Status = $"error: you don't have permissions to edit variable value(s) for environment '{propertyValueDto.PropertyValueFilter}'"
                    };
                }

                var dbPropertyValueModel = FindExistingPropertyValue(propertyValueDto);

                if (dbPropertyValueModel?.Id == null)
                {
                    return new Response
                    {
                        Item = propertyValueDto,
                        Status = "Error: variable value does not exist - please use Post to create it"
                    };
                }

                if (propertyValueDto.Property.Secure != dbPropertyValueModel.Property.Secure)
                {
                    return new Response
                    {
                        Item = propertyValueDto,
                        Status = "Error: property metadata - secure values don't match, and cannot be edited via this API"
                    };
                }

                var propertyValueToUpdate = propertyValueDto.Property.Secure
                    ? _propertyEncryptor.EncryptValue(propertyValueDto.Value)
                    : propertyValueDto.Value;
                _propertyValuesPersistentSource.UpdatePropertyValue(dbPropertyValueModel.Id, propertyValueToUpdate);

                if (dbPropertyValueModel.Value != propertyValueToUpdate)
                {
                    AuditPropertyValueChange(dbPropertyValueModel, dbPropertyValueModel.Value, propertyValueToUpdate, "Update", user);
                }

                return new Response { Item = propertyValueDto, Status = "success" };
            }
            catch (Exception e)
            {
                _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod().Name} with argument: {propertyValueDto.Property.Name} failed: {e.Message}");
                return new Response { Item = propertyValueDto, Status = e.Message };
            }
        }

        private PropertyValueDto? FindExistingPropertyValue(PropertyValueDto propertyValueDto)
        {
            var propertyValuesByName = _propertyValuesPersistentSource
                .GetPropertyValuesByName(propertyValueDto.Property.Name);

            return propertyValueDto.PropertyValueFilter == null
                ? propertyValuesByName.FirstOrDefault(pv => pv.PropertyValueFilterId == null)
                : propertyValuesByName.FirstOrDefault(pv =>
                    pv.PropertyValueFilter != null &&
                    pv.PropertyValueFilter.Equals(propertyValueDto.PropertyValueFilter,
                        StringComparison.CurrentCultureIgnoreCase));
        }

        private void AuditPropertyValueChange(PropertyValueDto propertyValue, string oldValue, string newValue, string operationType, ClaimsPrincipal user)
        {
            if (propertyValue == null)
                return;

            var propertyApiModel = _propertiesPersistentSource.GetProperty(propertyValue.Property.Name);
            string username = _claimsPrincipalReader.GetUserFullDomainName(user);
            _propertyValuesAuditPersistentSource.AddRecord(propertyApiModel.Id,
                propertyValue.Id, propertyValue.Property.Name,
                propertyValue.PropertyValueFilter, oldValue, newValue,
                username, operationType);
        }
    }
}