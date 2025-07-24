using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{
    public class PropertiesPersistentSource : IPropertiesPersistentSource
    {
        private readonly ILogger<PropertiesPersistentSource> _log;
        private readonly IDeploymentContextFactory _contextFactory;

        public PropertiesPersistentSource(IDeploymentContextFactory contextFactory, ILogger<PropertiesPersistentSource> logger)
        {
            _contextFactory = contextFactory;
            _log = logger;
        }

        public PropertyApiModel CreateProperty(string propertyName, bool secure, string updatedBy)
        {
            using (var context = _contextFactory.GetContext())
            {
                if (!context.Properties.Any(x => x.Name == propertyName))
                {
                    var property = new Property { Name = propertyName, Secure = secure };

                    _log.LogInformation("Adding Property " + propertyName);
                    context.Properties.Add(property);

                    _log.LogInformation("Saving into the Database");
                    context.SaveChanges();

                    var prop = context.Properties.First(p => p.Name.Equals(propertyName));

                    context.AuditProperties.Add(new AuditProperty
                    {
                        FromValue = "",
                        PropertyId = prop.Id,
                        PropertyName =
                            prop.Name,
                        ToValue = prop.Name,
                        Type = "Insert",
                        UpdatedDate = DateTime.Now,
                        UpdatedBy = updatedBy
                    });
                    context.SaveChanges();

                    return MapToPropertyApiModel(prop);
                }

                _log.LogWarning("Property " + propertyName + " Already Exists");
                return MapToPropertyApiModel(context.Properties.First(p => p.Name.Equals(propertyName)));
            }
        }

        public IEnumerable<PropertyApiModel> GetAllProperties()
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.Properties
                    .Select(MapToPropertyApiModel).ToList();
            }
        }

        public IQueryable<PropertyApiModel> GetAllPropertiesAsQueryable()
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.Properties.Select(MapToPropertyApiModel).AsQueryable();
            }
        }


        public bool DeleteProperty(string propertyName, string updatedBy)
        {
            using (var context = _contextFactory.GetContext())
            {
                var property = context.Properties.FirstOrDefault(p => p.Name.Equals(propertyName));
                if (property == null)
                    return false;
                context.Properties.Remove(property);
                context.AuditProperties.Add(new AuditProperty
                {
                    FromValue = "",
                    PropertyId = property.Id,
                    PropertyName =
                        property.Name,
                    ToValue = string.Empty,
                    Type = "Delete",
                    UpdatedDate = DateTime.Now,
                    UpdatedBy = updatedBy
                });
                context.SaveChanges();
                return true;
            }
        }

        public PropertyApiModel CreateProperty(PropertyApiModel property, string updatedBy)
        {
            using (var context = _contextFactory.GetContext())
            {
                var foundProperty = context.Properties.FirstOrDefault(p => p.Name.Equals(property.Name));
                if (foundProperty != null)
                    return MapToPropertyApiModel(context.Properties.FirstOrDefault(p => p.Name.Equals(property.Name)));

                context.Properties.Add(MapToProperty(property));
                context.SaveChanges();

                var prop = context.Properties.FirstOrDefault(p => p.Name.Equals(property.Name));

                if (prop == null)
                    return null;

                context.AuditProperties.Add(new AuditProperty
                {
                    FromValue = "",
                    PropertyId = prop.Id,
                    PropertyName =
                        prop.Name,
                    ToValue = prop.Name,
                    Type = "Insert",
                    UpdatedDate = DateTime.Now,
                    UpdatedBy = updatedBy
                });
                context.SaveChanges();

                return MapToPropertyApiModel(prop);

            }
        }

        public bool UpdateProperty(PropertyApiModel prop)
        {
            using (var context = _contextFactory.GetContext())
            {
                var property = context.Properties.FirstOrDefault(p => p.Name.Equals(prop.Name));
                if (property == null)
                    return false;

                MapToProperty(prop, property);
                context.SaveChanges();
                return true;

            }
        }

        public PropertyApiModel GetProperty(string propertyName)
        {
            using (var context = _contextFactory.GetContext())
            {
                return MapToPropertyApiModel(context.Properties.FirstOrDefault(p => p.Name.Equals(propertyName)));
            }
        }

        public PropertyApiModel GetProperty(int propertyId)
        {
            using (var context = _contextFactory.GetContext())
            {
                return MapToPropertyApiModel(context.Properties.FirstOrDefault(p => p.Id == propertyId));
            }
        }

        public IEnumerable<PropertyApiModel> GetPropertiesContaining(string containingText)
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.Properties.Where(p => TestForPropertyNameContaining(p, containingText)
                ).ToList().Select(MapToPropertyApiModel).ToList();
            }
        }

        private bool TestForPropertyNameContaining(Property p, string containingText)
        {
            return string.IsNullOrEmpty(containingText) || p.Name.Equals(containingText);
        }

        public bool IsPropertySecure(string propertyName)
        {
            using (var context = _contextFactory.GetContext())
            {
                try
                {
                    var property = context.Properties.FirstOrDefault(p =>
                        EF.Functions.Collate(p.Name, DeploymentContext.CaseInsensitiveCollation)
                            == EF.Functions.Collate(propertyName, DeploymentContext.CaseInsensitiveCollation));
                    return property != null && property.Secure;
                }
                catch
                {
                    return false;
                }
            }
        }

        public string GetConfigurationFilePath(EnvironmentApiModel environment)
        {
            using (var context = _contextFactory.GetContext())
            {
                var env = context.Environments
                    .Include(d => d.Databases)
                    .FirstOrDefault(e => e.Id == environment.EnvironmentId);
                if (env != null)
                {
                    var database = env.Databases.SingleOrDefault(d => d.Type == "Endur");
                    if (database == null) return null;

                    var shortName = GetEnvironmentShortNameFromDatabaseName(database.Name);
                    var cfgFilename = $"ENDUR_{shortName}.cfg";

                    return Path.Combine(environment.Details.FileShare, "Resources", cfgFilename);
                }
            }

            return string.Empty;
        }

        private static string GetEnvironmentShortNameFromDatabaseName(string databaseName)
        {
            return databaseName.Replace("_DB", "");
        }

        PropertyApiModel MapToPropertyApiModel(Property prop)
        {
            if (prop == null) return null;

            return new PropertyApiModel
            {
                Id = prop.Id,
                Name = prop.Name,
                Secure = prop.Secure,
                IsArray = prop.IsArray
            };
        }

        Property MapToProperty(PropertyApiModel prop)
        {
            return new Property
            {
                Id = prop.Id,
                Name = prop.Name,
                Secure = prop.Secure
            };
        }

        Property MapToProperty(PropertyApiModel prop, Property p)
        {
            p.Id = prop.Id;
            p.Name = prop.Name;
            p.Secure = prop.Secure;
            return p;
        }
    }
}
