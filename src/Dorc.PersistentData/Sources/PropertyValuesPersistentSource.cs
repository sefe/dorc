using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Security.Principal;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using Environment = Dorc.PersistentData.Model.Environment;
using Property = Dorc.PersistentData.Model.Property;

namespace Dorc.PersistentData.Sources
{
    public class PropertyValuesPersistentSource : IPropertyValuesPersistentSource
    {
        private const string EnvironmentPropertyFilterType = "Environment";
        private readonly IDictionary<string, PropertyValueDto> _cachedProperties = new ConcurrentDictionary<string, PropertyValueDto>();
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly IPropertyEncryptor _encrypt;
        private readonly Dictionary<PropertyFilter, string> _filters = new Dictionary<PropertyFilter, string>();
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public PropertyValuesPersistentSource(
            IDeploymentContextFactory contextFactory,
            IPropertyEncryptor propertyEncrypt,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            _contextFactory = contextFactory;
            _encrypt = propertyEncrypt;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        public bool Remove(long? propertyValueId)
        {
            using (var context = _contextFactory.GetContext())
            {
                try
                {
                    var propertyValue = context.PropertyValues.Include(pv => pv.Filters)
                        .FirstOrDefault(pv => pv.Id == propertyValueId);
                    if (propertyValue == null)
                        return false;

                    foreach (var propertyValueFilter in propertyValue.Filters)
                    {
                        context.PropertyValueFilters.Remove(propertyValueFilter);
                    }
                    context.PropertyValues.Remove(propertyValue);
                    context.SaveChanges();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool RemoveByFilterId(long? propertyValueFilterId)
        {
            using (var context = _contextFactory.GetContext())
            {
                try
                {
                    var propertyValueFilter = context.PropertyValueFilters.Include(pvf => pvf.PropertyValue)
                        .FirstOrDefault(pvf => pvf.Id == propertyValueFilterId);
                    if (propertyValueFilter == null)
                        return false;

                    var pv = propertyValueFilter.PropertyValue;

                    context.PropertyValueFilters.Remove(propertyValueFilter);
                    context.PropertyValues.Remove(pv);
                    context.SaveChanges();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public void AddFilter(string filterName, string value)
        {
            using (var context = _contextFactory.GetContext())
            {
                _filters.Add(context.PropertyFilters.Single(f => f.Name == filterName), value);
            }
        }

        public PropertyValueDto Get(long? propertyValueId)
        {
            using (var context = _contextFactory.GetContext())
            {
                return MapToPropertyValueDto(context.PropertyValues.Include(i => i.Property).Include(i => i.Filters)
                    .First(q => q.Id == propertyValueId));
            }
        }

        public PropertyValueDto UpdatePropertyValue(long? propertyValueId, string newValue)
        {
            using (var context = _contextFactory.GetContext())
            {
                var propertyValue = context.PropertyValues
                    .Include(pv => pv.Filters)
                    .Include(pv => pv.Property)
                    .First(pv => pv.Id == propertyValueId);
                propertyValue.Value = newValue;

                context.SaveChanges();
                return MapToPropertyValueDto(propertyValue);
            }
        }

        public List<PropertyValueDto> GetPropertyValues(string propertyName, string environmentName, bool decryptProperty)
        {
            using (var context = _contextFactory.GetContext())
            {
                if (!string.IsNullOrEmpty(environmentName))
                {
                    var envProps = GetEnvironmentProperties(environmentName, propertyName).ToList();
                    
                    if (!envProps.Any(p => p.Property.Name == propertyName))
                    {
                        var environmentSecure = context.Environments.First(e => e.Name.Equals(environmentName)).Secure;
                        if (!environmentSecure)
                        {
                            var globalProperties = GetGlobalProperties(propertyName);
                            envProps.AddRange(globalProperties);
                        }
                    }

                    return envProps.Select(x => decryptProperty ? DecryptPropertyValue(ref x) : x).ToList();
                }
                else {
                    var values = context.PropertyValues
                        .Include(pv => pv.Filters)
                        .ThenInclude(f => f.PropertyFilter)
                        .Include(pv => pv.Property)
                        .Where(pv => pv.Property.Name == propertyName).ToList();

                    if (!values.Any())
                    {
                        return new List<PropertyValueDto>();
                    }

                    var res = values.Where(x => !x.Filters.Any());
                    return res.Select(x => MapToPropertyValueDto(x, decryptProperty)).ToList();                    
                }
            }
        }

        public void ReassignPropertyValues(IDeploymentContext context, Environment oldEnv, string newEnvName)
        {
            var propertyValueFilters = context.PropertyValueFilters.Where(pvf => pvf.Value == oldEnv.Name).ToList();

            foreach (var propertyValueFilter in propertyValueFilters)
            {
                propertyValueFilter.Value = newEnvName;
            }
        }

        public PropertyValueDto AddPropertyValue(PropertyValueDto propertyValueDto)
        {
            using (var context = _contextFactory.GetContext())
            {
                var property = context.Properties.First(p =>
                    EF.Functions.Collate(p.Name, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(propertyValueDto.Property.Name, DeploymentContext.CaseInsensitiveCollation));
                var propertyValue = MapToPropertyValue(propertyValueDto, property);
                if (property.Secure)
                {
                    if (property.IsArray)
                    {
                        var encrypted = JsonSerializer.Deserialize<string[]>(propertyValueDto.Value)
                            ?.Select(s => _encrypt.EncryptValue(propertyValue.Value)).ToList();

                        propertyValue.Value = JsonSerializer.Serialize(encrypted);
                    }
                    else
                    {
                        propertyValue.Value = _encrypt.EncryptValue(propertyValue.Value);
                    }
                }
                context.PropertyValues.Add(propertyValue);

                if (!string.IsNullOrWhiteSpace(propertyValueDto.PropertyValueFilter))
                    context.PropertyValueFilters.Add(new PropertyValueFilter
                    {
                        PropertyValue = propertyValue,
                        PropertyFilter = context.PropertyFilters.Find(1),
                        Value = propertyValueDto.PropertyValueFilter,

                    });
                context.SaveChanges();
                return MapToPropertyValueDto(propertyValue);
            }
        }

        public PropertyValueDto[] GetPropertyValuesByName(string propertyName)
        {
            using (var context = _contextFactory.GetContext())
            {
                var ds = context.GetPropertyValuesByName(propertyName);
                var result = new PropertyValueDto[ds.Tables[0].Rows.Count];
                for (var i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    result[i] = new PropertyValueDto
                    {
                        Property = new PropertyApiModel
                        {
                            Name = ds.Tables[0].Rows[i][0].ToString(),
                            Secure = (bool)ds.Tables[0].Rows[i][1],
                            IsArray = (bool)ds.Tables[0].Rows[i][2]
                        },
                        Value = ds.Tables[0].Rows[i][3].ToString(),
                        PropertyValueFilter = ds.Tables[0].Rows[i][4] as string,
                        Id = ds.Tables[0].Rows[i][5] is long ? (long)ds.Tables[0].Rows[i][5] : 0,
                        PropertyValueFilterId = ds.Tables[0].Rows[i][6] as long?
                    };
                }
                return result;
            }
        }

        public PropertyValueDto GetCachedPropertyValue(string propertyName)
        {
            if (_cachedProperties.ContainsKey(propertyName))
                return _cachedProperties[propertyName];

            return null;
        }

        public void AddEnvironmentFilter(string envName)
        {
            using (var context = _contextFactory.GetContext())
            {
                var filter = context.PropertyFilters.Single(f => f.Name == EnvironmentPropertyFilterType);
                _filters.Add(filter, envName);

                //???? Is Save needed?
            }
        }

        public PropertyValueDto[] GetGlobalProperties(string? propertyName = null)
        {
            using (var context = _contextFactory.GetContext())
            {
                var ds = context.GetGlobalProperties(propertyName);
                var result = new PropertyValueDto[ds.Tables[0].Rows.Count];
                for (var i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    var pv = new PropertyValueDto
                    {
                        Property = new PropertyApiModel
                        {
                            Name = ds.Tables[0].Rows[i][0].ToString(),
                            Secure = (bool)ds.Tables[0].Rows[i][1],
                            IsArray = (bool)ds.Tables[0].Rows[i][2]
                        },
                        Value = ds.Tables[0].Rows[i][3].ToString()
                    };
                    result[i] = pv;
                }
                return result;
            }
        }

        public PropertyValueDto[] GetEnvironmentProperties(string environmentName, string? propertyName = null)
        {
            using (var context = _contextFactory.GetContext())
            {
                var ds = context.GetEnvironmentProperties(environmentName, propertyName);
                var result = new PropertyValueDto[ds.Tables[0].Rows.Count];
                for (var i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    var propValue = new PropertyValueDto
                    {
                        Property = new PropertyApiModel
                        {
                            Name = ds.Tables[0].Rows[i][0].ToString(),
                            Secure = (bool)ds.Tables[0].Rows[i][1],
                            IsArray = (bool)ds.Tables[0].Rows[i][2]
                        },
                        Value = ds.Tables[0].Rows[i][3].ToString(),
                        PropertyValueFilter = ds.Tables[0].Rows[i][4].ToString(),
                        Priority = (int)ds.Tables[0].Rows[i][5]
                    };
                    result[i] = propValue;
                }

                return result;
            }
        }

        public PropertyValueDto[] GetPropertyValuesForUser(string? environmentName, string? propertyName, string username, string sidList)
        {
            if (environmentName is null && propertyName is null)
                throw new ArgumentException("Both environmentName and propertyName cannot be null");

            using (var context = _contextFactory.GetContext())
            {
                var ds = context.GetPropertyValuesForUser(environmentName, propertyName, username, sidList);
                return getPropertyValueDtosForUser(ds);
            }
        }

        public bool IsCachedPropertySecure(string propertyName)
        {
            return _cachedProperties.ContainsKey(propertyName) && _cachedProperties[propertyName].Property.Secure;
        }

        public IDictionary<string, PropertyValueDto> LoadAllPropertiesIntoCache()
        {
            using (var context = _contextFactory.GetContext())
            {
                var envName = _filters.FirstOrDefault(f => f.Key.Name == EnvironmentPropertyFilterType).Value;
                var environmentSecure = false;
                if (!string.IsNullOrEmpty(envName))
                {
                    environmentSecure = context.Environments.First(e => e.Name.Equals(envName)).Secure;
                }

                var globalPropertiesAsync = GetGlobalPropertiesAsync(environmentSecure);

                var propertiesForEnvironmentAsync = Task.FromResult<IDictionary<string, PropertyValueDto>>(null);
                if (!string.IsNullOrEmpty(envName))
                {
                    propertiesForEnvironmentAsync = GetPropertiesForEnvironmentAsync();
                }

                Task.WaitAll(globalPropertiesAsync, propertiesForEnvironmentAsync);

                foreach (var kvp in globalPropertiesAsync.Result)
                {
                    AddKeyPair(_cachedProperties, kvp.Key, kvp.Value);
                }

                var envProps = propertiesForEnvironmentAsync.Result;
                if (envProps == null)
                    return _cachedProperties;

                foreach (var kvp in propertiesForEnvironmentAsync.Result)
                {
                    AddKeyPair(_cachedProperties, kvp.Key, kvp.Value);
                }

                return _cachedProperties;
            }
        }

        public GetScopedPropertyValuesResponseDto GetPropertyValuesForScopeByPage(int limit, int page,
            PagedDataOperators operators, EnvironmentApiModel scope, IPrincipal user)
        {
            string username = _claimsPrincipalReader.GetUserLogin(user);
            var userSids = _claimsPrincipalReader.GetSidsForUser(user);

            PagedModel<FlatPropertyValueApiModel> output = null;
            using (var context = _contextFactory.GetContext())
            {
                var envProps = from propertyValue in context.PropertyValues
                               join property in context.Properties on propertyValue.Property.Id equals property.Id
                               join propertyValueFilter in context.PropertyValueFilters on propertyValue.Id equals
                                   propertyValueFilter.PropertyValue.Id
                               join propertyFilter in context.PropertyFilters on propertyValueFilter.PropertyFilter.Id equals
                                   propertyFilter.Id
                               join environment in context.Environments on propertyValueFilter.Value equals
                                   environment.Name
                               let isDelegate =
                                   (from env in context.Environments
                                    where env.Name == environment.Name && env.Users.Select(u => u.LoginId).Contains(username)
                                    select env.Name).Any()
                               let permissions =
                                   (from env in context.Environments
                                    join ac in context.AccessControls on env.ObjectId equals ac.ObjectId
                                    where env.Name == environment.Name && (userSids.Contains(ac.Sid) || ac.Pid != null && userSids.Contains(ac.Pid))
                                    select ac.Allow).ToList()
                               let hasPermission =
                                   permissions.Any(p => (p & (int)(AccessLevel.Write | AccessLevel.Owner)) != 0)
                               let isOwner =
                                   permissions.Any(p => (p & (int)(AccessLevel.Owner)) != 0)
                               where propertyFilter.Name == "environment" && propertyValueFilter.Value == scope.EnvironmentName
                               select new FlatPropertyValueApiModel
                               {
                                   PropertyId = property.Id,
                                   Property = property.Name,
                                   PropertyValueScope = propertyValueFilter.Value,
                                   PropertyValueScopeId = propertyValueFilter.Id,
                                   PropertyValue = propertyValue.Value,
                                   PropertyValueId = propertyValue.Id,
                                   Secure = property.Secure,
                                   IsArray = property.IsArray,
                                   UserEditable = isOwner || isDelegate || hasPermission
                               };

                IQueryable<FlatPropertyValueApiModel> scopedPropertyValuesQuery;
                if (scope.EnvironmentSecure)
                {
                    scopedPropertyValuesQuery = envProps;
                }
                else
                {
                    var global = from propertyValue in context.PropertyValues
                                 join property in context.Properties on propertyValue.Property.Id equals property.Id
                                 join propertyValueFilter in context.PropertyValueFilters on propertyValue.Id equals propertyValueFilter.PropertyValue.Id into tmp
                                 from final in tmp.DefaultIfEmpty()
                                 where final == null
                                 select new FlatPropertyValueApiModel
                                 {
                                     PropertyId = property.Id,
                                     Property = property.Name,
                                     PropertyValueScope = null,
                                     PropertyValueScopeId = -1,
                                     PropertyValue = propertyValue.Value,
                                     PropertyValueId = propertyValue.Id,
                                     Secure = property.Secure,
                                     IsArray = property.IsArray,
                                     UserEditable = false // admin privileges are set at the calling fn
                                 };

                    var parentEnv = context.Environments.FirstOrDefault(e => e.Id == scope.ParentId);
                    if (parentEnv is not null)
                    {
                        var envParentProps = from propertyValue in context.PropertyValues
                                             join property in context.Properties on propertyValue.Property.Id equals property.Id
                                             join propertyValueFilter in context.PropertyValueFilters on propertyValue.Id equals
                                                 propertyValueFilter.PropertyValue.Id
                                             join propertyFilter in context.PropertyFilters on propertyValueFilter.PropertyFilter.Id equals
                                                 propertyFilter.Id
                                             join environment in context.Environments on propertyValueFilter.Value equals
                                                 environment.Name
                                             where propertyFilter.Name == "environment" && (parentEnv.Name == propertyValueFilter.Value)
                                             select new FlatPropertyValueApiModel
                                             {
                                                 PropertyId = property.Id,
                                                 Property = property.Name,
                                                 PropertyValueScope = propertyValueFilter.Value,
                                                 PropertyValueScopeId = propertyValueFilter.Id,
                                                 PropertyValue = propertyValue.Value,
                                                 PropertyValueId = propertyValue.Id,
                                                 Secure = property.Secure,
                                                 IsArray = property.IsArray,
                                                 UserEditable = false // Parent props not allowed to edit
                                             };

                        // Union the parent environment properties with the current environment properties and with global
                        scopedPropertyValuesQuery = envProps
                            .Union(envParentProps.Where(p => !envProps.Any(ep => ep.Property == p.Property)))
                            .Union(global.Where(g => !envProps.Any(ep => ep.Property == g.Property) && !envParentProps.Any(pp => pp.Property == g.Property)));
                    }
                    else
                    {
                        scopedPropertyValuesQuery = envProps
                            .Union(global.Where(g => !envProps.Any(ep => ep.Property == g.Property)));
                    }                    
                }

                if (operators.Filters != null && operators.Filters.Any())
                {
                    foreach (var pagedDataFilter in operators.Filters)
                    {
                        if (pagedDataFilter == null)
                            continue;
                        if (string.IsNullOrEmpty(pagedDataFilter.Path) ||
                            string.IsNullOrEmpty(pagedDataFilter.FilterValue))
                            continue;

                        scopedPropertyValuesQuery =
                            scopedPropertyValuesQuery.Where(scopedPropertyValuesQuery.ContainsExpression(pagedDataFilter.Path,
                                pagedDataFilter.FilterValue));
                    }
                }

                if (operators.SortOrders != null && operators.SortOrders.Any())
                {
                    IOrderedQueryable<FlatPropertyValueApiModel> orderedQuery = null;

                    for (var i = 0; i < operators.SortOrders.Count; i++)
                    {
                        if (operators.SortOrders[i] == null)
                            continue;
                        if (string.IsNullOrEmpty(operators.SortOrders[i].Path) ||
                            string.IsNullOrEmpty(operators.SortOrders[i].Direction))
                            continue;

                        var param = Expression.Parameter(typeof(FlatPropertyValueApiModel), "FlatPropertyValueApiModel");
                        var prop = Expression.PropertyOrField(param, operators.SortOrders[i].Path);

                        switch (prop.Type)
                        {
                            case Type boolType when boolType == typeof(bool):
                                {
                                    var expr = GetExpressionForOrdering<bool>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, scopedPropertyValuesQuery, expr);
                                    break;
                                }
                            case Type stringType when stringType == typeof(string):
                                {
                                    var expr = GetExpressionForOrdering<string>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, scopedPropertyValuesQuery, expr);
                                    break;
                                }
                        }
                    }

                    if (orderedQuery != null)
                        output = orderedQuery.AsNoTracking()
                            .Paginate(page, limit);
                }

                if (output == null)
                    output = scopedPropertyValuesQuery.AsNoTracking()
                        .OrderBy(s => s.Property)
                        .Paginate(page, limit);

                return new GetScopedPropertyValuesResponseDto
                {
                    CurrentPage = output.CurrentPage,
                    TotalPages = output.TotalPages,
                    TotalItems = output.TotalItems,
                    Items = output.Items.Select(p => p
                    ).ToList()
                };
            }
        }

        public GetScopedPropertyValuesResponseDto GetPropertyValuesForSearchValueByPage(int limit, int page,
            PagedDataOperators operators, IPrincipal user)
        {
            string username = _claimsPrincipalReader.GetUserLogin(user);
            var userSids = _claimsPrincipalReader.GetSidsForUser(user);

            PagedModel<FlatPropertyValueApiModel> output = null;
            using (var context = _contextFactory.GetContext())
            {
                IQueryable<FlatPropertyValueApiModel> scopedPropertyValuesQuery;
                {
                    var envProps = from propertyValue in context.PropertyValues
                                   join property in context.Properties on propertyValue.Property.Id equals property.Id
                                   join propertyValueFilter in context.PropertyValueFilters on propertyValue.Id equals
                                       propertyValueFilter.PropertyValue.Id
                                   join propertyFilter in context.PropertyFilters on propertyValueFilter.PropertyFilter.Id equals
                                       propertyFilter.Id
                                   join environment in context.Environments on propertyValueFilter.Value equals
                                       environment.Name
                                   let isDelegate =
                                       (from env in context.Environments
                                        where env.Name == environment.Name && env.Users.Select(u => u.LoginId).Contains(username)
                                        select env.Name).Any()
                                   let permissions =
                                       (from env in context.Environments
                                        join ac in context.AccessControls on env.ObjectId equals ac.ObjectId
                                        where env.Name == environment.Name && (userSids.Contains(ac.Sid) || ac.Pid != null && userSids.Contains(ac.Pid))
                                        select ac.Allow).ToList()
                                   let hasPermission =
                                       permissions.Any(p => (p & (int)(AccessLevel.Write | AccessLevel.Owner)) != 0)
                                   let isOwner =
                                       permissions.Any(p => (p & (int)(AccessLevel.Owner)) != 0)
                                   select new FlatPropertyValueApiModel
                                   {
                                       PropertyId = property.Id,
                                       Property = property.Name,
                                       PropertyValueScope = propertyValueFilter.Value,
                                       PropertyValueScopeId = propertyValueFilter.Id,
                                       PropertyValue = propertyValue.Value,
                                       PropertyValueId = propertyValue.Id,
                                       Secure = property.Secure,
                                       IsArray = property.IsArray,
                                       UserEditable = isOwner || isDelegate || hasPermission
                                   };

                    var global = from propertyValue in context.PropertyValues
                                 join property in context.Properties on propertyValue.Property.Id equals property.Id
                                 join propertyValueFilter in context.PropertyValueFilters on propertyValue.Id equals propertyValueFilter.PropertyValue.Id into tmp
                                 from final in tmp.DefaultIfEmpty()
                                 where final == null
                                 select new FlatPropertyValueApiModel
                                 {
                                     PropertyId = property.Id,
                                     Property = property.Name,
                                     PropertyValueScope = null,
                                     PropertyValueScopeId = -1,
                                     PropertyValue = propertyValue.Value,
                                     PropertyValueId = propertyValue.Id,
                                     Secure = property.Secure,
                                     IsArray = property.IsArray,
                                     UserEditable = false // admin privileges are set at the calling fn
                                 };

                    scopedPropertyValuesQuery = envProps.Union(global);
                }

                if (operators.Filters != null && operators.Filters.Any())
                {
                    foreach (var pagedDataFilter in operators.Filters)
                    {
                        if (pagedDataFilter == null)
                            continue;
                        if (string.IsNullOrEmpty(pagedDataFilter.Path) ||
                            string.IsNullOrEmpty(pagedDataFilter.FilterValue))
                            continue;

                        scopedPropertyValuesQuery =
                            scopedPropertyValuesQuery.Where(scopedPropertyValuesQuery.ContainsExpression(pagedDataFilter.Path,
                                pagedDataFilter.FilterValue));
                    }
                }

                if (operators.SortOrders != null && operators.SortOrders.Any())
                {
                    IOrderedQueryable<FlatPropertyValueApiModel> orderedQuery = null;

                    for (var i = 0; i < operators.SortOrders.Count; i++)
                    {
                        if (operators.SortOrders[i] == null)
                            continue;
                        if (string.IsNullOrEmpty(operators.SortOrders[i].Path) ||
                            string.IsNullOrEmpty(operators.SortOrders[i].Direction))
                            continue;

                        var param = Expression.Parameter(typeof(FlatPropertyValueApiModel), "FlatPropertyValueApiModel");
                        var prop = Expression.PropertyOrField(param, operators.SortOrders[i].Path);

                        switch (prop.Type)
                        {
                            case Type boolType when boolType == typeof(bool):
                                {
                                    var expr = GetExpressionForOrdering<bool>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, scopedPropertyValuesQuery, expr);
                                    break;
                                }
                            case Type stringType when stringType == typeof(string):
                                {
                                    var expr = GetExpressionForOrdering<string>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, scopedPropertyValuesQuery, expr);
                                    break;
                                }
                        }
                    }

                    if (orderedQuery != null)
                        output = orderedQuery.AsNoTracking()
                            .Paginate(page, limit);
                }

                if (output == null)
                    output = scopedPropertyValuesQuery.AsNoTracking()
                        .OrderBy(s => s.Property)
                        .Paginate(page, limit);

                return new GetScopedPropertyValuesResponseDto
                {
                    CurrentPage = output.CurrentPage,
                    TotalPages = output.TotalPages,
                    TotalItems = output.TotalItems,
                    Items = output.Items.Select(p => p
                    ).ToList()
                };
            }
        }

        private static IOrderedQueryable<FlatPropertyValueApiModel> OrderScripts<T>(PagedDataOperators operators, int i, IOrderedQueryable<FlatPropertyValueApiModel> orderedQuery,
            IQueryable<FlatPropertyValueApiModel> scriptsQuery, Expression<Func<FlatPropertyValueApiModel, T>> expr)
        {
            if (i == 0)
                switch (operators.SortOrders[i].Direction)
                {
                    case "asc":
                        orderedQuery = scriptsQuery.OrderBy(expr);
                        break;

                    case "desc":
                        orderedQuery = scriptsQuery.OrderByDescending(expr);
                        break;
                }
            else
                switch (operators.SortOrders[i].Direction)
                {
                    case "asc":
                        orderedQuery = orderedQuery?.ThenBy(expr);
                        break;

                    case "desc":
                        orderedQuery = orderedQuery?.ThenByDescending(expr);
                        break;
                }

            return orderedQuery;
        }

        private static Expression<Func<FlatPropertyValueApiModel, TR>> GetExpressionForOrdering<TR>(Expression prop, ParameterExpression param)
        {
            return Expression.Lambda<Func<FlatPropertyValueApiModel, TR>>(prop, param);
        }

        private Task<IDictionary<string, PropertyValueDto>> GetPropertiesForEnvironmentAsync()
        {
            var task = new Task<IDictionary<string, PropertyValueDto>>(() =>
            {
                var properties = new Dictionary<string, PropertyValueDto>();
                var environmentProperties =
                    GetEnvironmentProperties(_filters.First(f => f.Key.Name == EnvironmentPropertyFilterType).Value);
                properties = getPropertiesValuesDict(environmentProperties);

                return properties;
            });

            task.Start();
            return task;
        }

        private Task<IDictionary<string, PropertyValueDto>> GetGlobalPropertiesAsync(bool environmentSecure)
        {
            var task = new Task<IDictionary<string, PropertyValueDto>>(() =>
            {
                var properties = new Dictionary<string, PropertyValueDto>();
                if (environmentSecure)
                    return properties;

                var globalProperties = GetGlobalProperties();
                properties = getPropertiesValuesDict(globalProperties);
                return properties;
            });

            task.Start();
            return task;
        }

        private Dictionary<string, PropertyValueDto> getPropertiesValuesDict(PropertyValueDto[] dtoProperties)
        {
            var properties = new Dictionary<string, PropertyValueDto>();
            foreach (var t in dtoProperties)
            {
                switch (t.Property.Secure)
                {
                    case true:
                        {
                            t.Value = _encrypt.DecryptValue(t.Value.ToString());
                            AddKeyPair(properties, t.Property.Name, t);
                            break;
                        }
                    case false:
                        AddKeyPair(properties, t.Property.Name, t);
                        break;
                }
            }

            return properties;
        }

        private PropertyValueDto MapToPropertyValueDto(PropertyValue pv, bool decryptProperty = false)
        {
            if (pv == null)
                return null;

            PropertyValueFilter propFilter = null;
            if (pv.Filters != null)
            {
                propFilter = pv.Filters.FirstOrDefault();
            }

            return new PropertyValueDto
            {
                Id = pv.Id,
                Value = decryptProperty ? GetPropertyValue(pv) : pv.Value,
                Property = new PropertyApiModel
                {
                    Id = pv.Property.Id,
                    Name = pv.Property.Name,
                    Secure = pv.Property.Secure,
                    IsArray = pv.Property.IsArray
                },
                PropertyValueFilter = propFilter?.Value,
                PropertyValueFilterId = propFilter?.Id,
            };
        }

        private static PropertyValue MapToPropertyValue(PropertyValueDto pv, Property prop)
        {
            return new PropertyValue
            {
                Value = prop.IsArray ? JsonSerializer.Serialize(pv.Value) : pv.Value.ToString(),
                Property = prop,
            };
        }

        private string? GetPropertyValue(PropertyValue propertyValue)
        {
            if (propertyValue.Property == null)
            {
                return propertyValue.Value;
            }

            if (propertyValue.Property.Secure
                && propertyValue.Value != null)
            {
                return _encrypt.DecryptValue(propertyValue.Value);
            }

            return propertyValue.Value;
        }

        private PropertyValueDto DecryptPropertyValue(ref PropertyValueDto propertyValue)
        {
            if (propertyValue.Property.Secure
                && propertyValue.Value != null)
            {
                propertyValue.Value = _encrypt.DecryptValue(propertyValue.Value);
            }

            return propertyValue;
        }

        private static void AddKeyPair(IDictionary<string, PropertyValueDto> properties, string key, PropertyValueDto value)
        {
            if (!properties.ContainsKey(key))
                properties.Add(key, value);
            else
                properties[key] = value;
        }

        private PropertyValueDto[] getPropertyValueDtosForUser(System.Data.DataSet ds)
        {
            var result = new PropertyValueDto[ds.Tables[0].Rows.Count];
            for (var i = 0; i < ds.Tables[0].Rows.Count; i++)
            {
                var isOwner = ds.Tables[0].Rows[i][7] as int?;
                var isDelegate = ds.Tables[0].Rows[i][8] as int?;
                var hasPermission = ds.Tables[0].Rows[i][9] as int?;

                result[i] = new PropertyValueDto
                {
                    Property = new PropertyApiModel
                    {
                        Name = ds.Tables[0].Rows[i][0].ToString(),
                        Secure = (bool)ds.Tables[0].Rows[i][1],
                        IsArray = (bool)ds.Tables[0].Rows[i][2]
                    },
                    Value = ds.Tables[0].Rows[i][3].ToString(),
                    PropertyValueFilter = ds.Tables[0].Rows[i][4] as string,
                    Id = ds.Tables[0].Rows[i][5] is long ? (long)ds.Tables[0].Rows[i][5] : 0,
                    PropertyValueFilterId = ds.Tables[0].Rows[i][6] as long?,
                    Priority = ds.Tables[0].Rows[i][10] as int? ?? 0,
                    UserEditable = isOwner == 1 || isDelegate == 1 || hasPermission == 1
                };
            }

            return result;
        }
    }
}
