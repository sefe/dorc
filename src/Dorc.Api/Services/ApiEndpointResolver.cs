using Dorc.ApiModel;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Services
{
    public interface IApiEndpointResolver
    {
        void ResolveEndpoint(ApiApiModel model, string environmentName);

        void ResolveEndpoints(IEnumerable<ApiApiModel> models, string environmentName);
    }

    public class ApiEndpointResolver : IApiEndpointResolver
    {
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly PropertyParser _parser = new();

        public ApiEndpointResolver(IPropertyValuesPersistentSource propertyValuesPersistentSource)
        {
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
        }

        public void ResolveEndpoint(ApiApiModel model, string environmentName)
        {
            var properties = LoadEnvironmentPropertyBag(environmentName);
            ApplyResolution(model, properties);
        }

        public void ResolveEndpoints(IEnumerable<ApiApiModel> models, string environmentName)
        {
            var list = models as IList<ApiApiModel> ?? models.ToList();
            if (list.Count == 0)
                return;

            var properties = LoadEnvironmentPropertyBag(environmentName);
            foreach (var model in list)
                ApplyResolution(model, properties);
        }

        private IDictionary<string, string> LoadEnvironmentPropertyBag(string environmentName)
        {
            if (string.IsNullOrEmpty(environmentName))
                return new Dictionary<string, string>(StringComparer.Ordinal);

            var bag = new Dictionary<string, string>(StringComparer.Ordinal);
            var properties = _propertyValuesPersistentSource.GetEnvironmentProperties(environmentName, null);
            if (properties == null)
                return bag;

            foreach (var pv in properties)
            {
                if (pv?.Property?.Name == null || pv.Value == null)
                    continue;
                if (pv.Property.Secure)
                    continue;
                bag[pv.Property.Name] = pv.Value;
            }
            return bag;
        }

        private void ApplyResolution(ApiApiModel model, IDictionary<string, string> properties)
        {
            if (string.IsNullOrEmpty(model.Endpoint))
            {
                model.EndpointResolved = string.Empty;
                model.ResolutionStatus = ApiEndpointResolutionStatus.NoTokens;
                model.UnresolvedTokens = null;
                return;
            }

            List<Token> tokens;
            try
            {
                tokens = _parser.Parse(model.Endpoint).ToList();
            }
            catch (InvalidOperationException ex)
            {
                model.EndpointResolved = model.Endpoint;
                model.ResolutionStatus = ApiEndpointResolutionStatus.PartiallyResolved;
                model.UnresolvedTokens = $"invalid placeholder syntax: {ex.Message}";
                return;
            }

            var sawToken = false;
            var unresolved = new List<string>();
            var sb = new System.Text.StringBuilder();

            foreach (var token in tokens)
            {
                switch (token)
                {
                    case PropertyToken propertyToken:
                        sawToken = true;
                        if (properties.TryGetValue(propertyToken.Value, out var value))
                        {
                            sb.Append(value);
                        }
                        else
                        {
                            unresolved.Add(propertyToken.Value);
                            sb.Append('$').Append(propertyToken.Value).Append('$');
                        }
                        break;
                    case StaticToken staticToken:
                        sb.Append(staticToken.Value);
                        break;
                }
            }

            model.EndpointResolved = sb.ToString();
            if (!sawToken)
                model.ResolutionStatus = ApiEndpointResolutionStatus.NoTokens;
            else if (unresolved.Count == 0)
                model.ResolutionStatus = ApiEndpointResolutionStatus.Resolved;
            else
                model.ResolutionStatus = ApiEndpointResolutionStatus.PartiallyResolved;

            model.UnresolvedTokens = unresolved.Count == 0 ? null : string.Join(",", unresolved);
        }
    }
}
