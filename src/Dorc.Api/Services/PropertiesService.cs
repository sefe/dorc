using System.Security.Claims;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;

namespace Dorc.Api.Services
{
    public class PropertiesService : IPropertiesService
    {
        private readonly ILog _log;
        private readonly IPropertiesPersistentSource _propertiesPersistentSource;
        private readonly IPropertyValuesService _propertyValuesService;

        public PropertiesService(IPropertiesPersistentSource propertiesPersistentSource, IPropertyValuesService propertyValuesService)
        {
            _propertyValuesService = propertyValuesService;
            _propertiesPersistentSource = propertiesPersistentSource;
            _log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        }

        public PropertyApiModel GetProperty(string propertyName)
        {
            try
            {
                return _propertiesPersistentSource.GetProperty(propertyName);
            }
            catch (Exception e)
            {
                _log.Error($"{System.Reflection.MethodBase.GetCurrentMethod().Name} with argument: {propertyName} failed: {e.Message}", e);
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
                _log.Error($"{System.Reflection.MethodBase.GetCurrentMethod().Name} failed: {e.Message}", e);
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

                    if (_propertiesPersistentSource.DeleteProperty(property, User.Identity.Name))
                    {
                        result.Add(new Response { Item = property, Status = "success" });
                        continue;
                    }
                    result.Add(new Response { Item = property, Status = "error: failed deletion from the database" });

                }
                catch (Exception e)
                {
                    _log.Error($"{System.Reflection.MethodBase.GetCurrentMethod().Name} failed: {e.Message}", e);
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
                    _propertiesPersistentSource.CreateProperty(property, User.Identity.Name);
                    result.Add(new Response { Item = property, Status = "success" });
                }
                catch (Exception e)
                {
                    _log.Error($"{System.Reflection.MethodBase.GetCurrentMethod().Name} failed: {e.Message}", e);
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
                    _log.Error($"{System.Reflection.MethodBase.GetCurrentMethod().Name} failed: {e.Message}", e);
                    result.Add(UnrollException(e, propertyUpdateEntry));
                }
            }

            return result;
        }
    }
}