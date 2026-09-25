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

            if (!canReadSecrets)
            {
                // Redaction is uniform, and both corrections here matter more than they did
                // before: restricting the privilege to service principals turns this from the
                // path a few callers take into the path every human takes.
                //
                // It previously raised a rights exception when EVERY value in the result was
                // secure, so a partial result redacted while a wholly-secure one failed. For
                // someone opening a property set that happens to contain only secure values,
                // that surfaced as an error rather than as masked data.
                //
                // And it was conditioned on an environment being named, so an unscoped query
                // redacted nothing - and because the decrypt branch is skipped too, the caller
                // received the stored CIPHERTEXT rather than either the plaintext or a blank.
                // Not a plaintext disclosure, but it hands encrypted material and value
                // presence to a caller not entitled to the value at all.
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
                try
                {
                    if (!_securityPrivilegesChecker.CanModifyPropertyValue(user, propertyValueDto.PropertyValueFilter))
                    {
                        result.Add(new Response
                        {
                            Item = propertyValueDto,
                            Status = "error: you don't have permissions to edit variable value(s) for this environment"
                        });
                        continue;
                    }

                    var propertyValuesByName = _propertyValuesPersistentSource
                        .GetPropertyValuesByName(propertyValueDto.Property.Name);
                    var filteredPropertyValues = string.IsNullOrEmpty(propertyValueDto.PropertyValueFilter)
                        ? propertyValuesByName.FirstOrDefault(pv => pv.PropertyValueFilter == null)
                        : propertyValuesByName.FirstOrDefault(pv =>
                            pv.PropertyValueFilter != null &&
                            pv.PropertyValueFilter.Equals(propertyValueDto.PropertyValueFilter,
                                StringComparison.CurrentCultureIgnoreCase));

                    if (filteredPropertyValues?.Id == 0)
                    {
                        result.Add(new Response
                        {
                            Item = propertyValueDto,
                            Status = "error: variable value does not exist"
                        });
                        continue;
                    }

                    if (propertyValueDto.PropertyValueFilter != null)
                    {
                        if (!_propertyValuesPersistentSource.RemoveByFilterId(filteredPropertyValues?.PropertyValueFilterId) &&
                            !_propertyValuesPersistentSource.Remove(
                                filteredPropertyValues?.Id))
                        {
                            result.Add(new Response
                            {
                                Item = propertyValueDto,
                                Status = "error: deletion from the database failed"
                            });
                            continue;
                        }
                    }
                    else
                    {
                        if (!_propertyValuesPersistentSource.Remove(filteredPropertyValues?.Id))
                        {
                            result.Add(new Response
                            {
                                Item = propertyValueDto,
                                Status = "error: deletion from the database failed"
                            });
                            continue;
                        }
                    }

                    var propertyApiModel = _propertiesPersistentSource.GetProperty(filteredPropertyValues?.Property.Name);
                    string username = _claimsPrincipalReader.GetUserFullDomainName(user);
                    if (filteredPropertyValues != null)
                        _propertyValuesAuditPersistentSource.AddRecord(propertyApiModel.Id,
                            filteredPropertyValues.Id, filteredPropertyValues.Property.Name,
                            filteredPropertyValues.PropertyValueFilter, filteredPropertyValues.Value, string.Empty,
                            username, "Delete");

                    result.Add(new Response { Item = propertyValueDto, Status = "success" });
                }
                catch (Exception e)
                {
                    _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod().Name} with argument: {propertyValueDto.Property.Name} failed: {e.Message}");
                    result.Add(new Response { Item = propertyValueDto, Status = e.Message });
                }

            return result;
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
                try
                {
                    if (!_securityPrivilegesChecker.CanModifyPropertyValue(user, propertyValueDto.PropertyValueFilter))
                    {
                        result.Add(new Response
                        {
                            Item = propertyValueDto,
                            Status = $"error: you don't have permissions to edit variable value(s) for environment '{propertyValueDto.PropertyValueFilter}'"
                        });
                        continue;
                    }


                    PropertyValueDto dbPropertyValueModel;
                    if (propertyValueDto.PropertyValueFilter == null)
                        dbPropertyValueModel = _propertyValuesPersistentSource
                            .GetPropertyValuesByName(propertyValueDto.Property.Name)
                            .FirstOrDefault(pv => pv.PropertyValueFilterId == null);
                    else
                        dbPropertyValueModel = _propertyValuesPersistentSource
                            .GetPropertyValuesByName(propertyValueDto.Property.Name)
                            .FirstOrDefault(pv =>
                                pv.PropertyValueFilter != null &&
                                pv.PropertyValueFilter.Equals(propertyValueDto.PropertyValueFilter,
                                    StringComparison.CurrentCultureIgnoreCase));

                    if (dbPropertyValueModel?.Id == null)
                    {
                        result.Add(new Response
                        {
                            Item = propertyValueDto,
                            Status = "Error: variable value does not exist - please use Post to create it"
                        });
                        continue;
                    }

                    if (propertyValueDto.Property.Secure != dbPropertyValueModel.Property.Secure)
                    {
                        result.Add(new Response
                        {
                            Item = propertyValueDto,
                            Status = "Error: property metadata - secure values don't match, and cannot be edited via this API"
                        });
                        continue;
                    }

                    var propertyValueToUpdate = propertyValueDto.Property.Secure
                        ? _propertyEncryptor.EncryptValue(propertyValueDto.Value)
                        : propertyValueDto.Value;
                    _propertyValuesPersistentSource.UpdatePropertyValue(dbPropertyValueModel.Id,
                        propertyValueToUpdate);
                    if (dbPropertyValueModel.Value == propertyValueToUpdate)
                        continue; // don't update if attempting to set the same value

                    var propertyApiModel = _propertiesPersistentSource.GetProperty(dbPropertyValueModel.Property.Name);
                    string username = _claimsPrincipalReader.GetUserFullDomainName(user);
                    _propertyValuesAuditPersistentSource.AddRecord(propertyApiModel.Id,
                        dbPropertyValueModel.Id, dbPropertyValueModel.Property.Name,
                        dbPropertyValueModel.PropertyValueFilter, dbPropertyValueModel.Value, propertyValueToUpdate,
                        username, "Update");

                    result.Add(new Response { Item = propertyValueDto, Status = "success" });
                }
                catch (Exception e)
                {
                    _log.LogError(e, $"{System.Reflection.MethodBase.GetCurrentMethod().Name} with argument: {propertyValueDto.Property.Name} failed: {e.Message}");
                    result.Add(new Response { Item = propertyValueDto, Status = e.Message });
                }

            return result;
        }
    }
}