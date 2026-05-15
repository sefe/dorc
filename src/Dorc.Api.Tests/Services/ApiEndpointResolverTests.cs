using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;

namespace Dorc.Api.Tests.Services
{
    [TestClass]
    public class ApiEndpointResolverTests
    {
        private IPropertyValuesPersistentSource _propertyValues;
        private ApiEndpointResolver _resolver;

        [TestInitialize]
        public void Setup()
        {
            _propertyValues = Substitute.For<IPropertyValuesPersistentSource>();
            _resolver = new ApiEndpointResolver(_propertyValues);
        }

        private static PropertyValueDto Property(string name, string value, bool secure = false)
        {
            return new PropertyValueDto
            {
                Value = value,
                Property = new PropertyApiModel { Name = name, Secure = secure }
            };
        }

        [TestMethod]
        public void ResolveEndpoint_AllTokensResolved_StatusResolved()
        {
            _propertyValues.GetEnvironmentProperties("ENV1", null).Returns(new[]
            {
                Property("ApiHost", "api-uat.example.com"),
                Property("ApiPort", "8443")
            });

            var model = new ApiApiModel { Endpoint = "https://$ApiHost$:$ApiPort$/v1" };
            _resolver.ResolveEndpoint(model, "ENV1");

            Assert.AreEqual("https://api-uat.example.com:8443/v1", model.EndpointResolved);
            Assert.AreEqual(ApiEndpointResolutionStatus.Resolved, model.ResolutionStatus);
            Assert.IsNull(model.UnresolvedTokens);
        }

        [TestMethod]
        public void ResolveEndpoint_MissingToken_StatusPartiallyResolved()
        {
            _propertyValues.GetEnvironmentProperties("ENV1", null).Returns(new[]
            {
                Property("ApiHost", "api-uat.example.com")
            });

            var model = new ApiApiModel { Endpoint = "https://$ApiHost$:$ApiPort$/v1" };
            _resolver.ResolveEndpoint(model, "ENV1");

            Assert.AreEqual("https://api-uat.example.com:$ApiPort$/v1", model.EndpointResolved);
            Assert.AreEqual(ApiEndpointResolutionStatus.PartiallyResolved, model.ResolutionStatus);
            Assert.AreEqual("ApiPort", model.UnresolvedTokens);
        }

        [TestMethod]
        public void ResolveEndpoint_NoTokens_StatusNoTokens()
        {
            _propertyValues.GetEnvironmentProperties("ENV1", null).Returns(Array.Empty<PropertyValueDto>());

            var model = new ApiApiModel { Endpoint = "https://api.example.com/v1" };
            _resolver.ResolveEndpoint(model, "ENV1");

            Assert.AreEqual("https://api.example.com/v1", model.EndpointResolved);
            Assert.AreEqual(ApiEndpointResolutionStatus.NoTokens, model.ResolutionStatus);
            Assert.IsNull(model.UnresolvedTokens);
        }

        [TestMethod]
        public void ResolveEndpoint_SecureProperty_NotSubstituted()
        {
            _propertyValues.GetEnvironmentProperties("ENV1", null).Returns(new[]
            {
                Property("ApiSecret", "shhhh", secure: true)
            });

            var model = new ApiApiModel { Endpoint = "https://api/$ApiSecret$" };
            _resolver.ResolveEndpoint(model, "ENV1");

            Assert.AreEqual("https://api/$ApiSecret$", model.EndpointResolved);
            Assert.AreEqual(ApiEndpointResolutionStatus.PartiallyResolved, model.ResolutionStatus);
            Assert.AreEqual("ApiSecret", model.UnresolvedTokens);
        }

        [TestMethod]
        public void ResolveEndpoint_EmptyEndpoint_NoTokensStatus()
        {
            _propertyValues.GetEnvironmentProperties("ENV1", null).Returns(Array.Empty<PropertyValueDto>());

            var model = new ApiApiModel { Endpoint = string.Empty };
            _resolver.ResolveEndpoint(model, "ENV1");

            Assert.AreEqual(string.Empty, model.EndpointResolved);
            Assert.AreEqual(ApiEndpointResolutionStatus.NoTokens, model.ResolutionStatus);
        }

        [TestMethod]
        public void ResolveEndpoints_BatchUsesSinglePropertyLoad()
        {
            _propertyValues.GetEnvironmentProperties("ENV1", null).Returns(new[]
            {
                Property("ApiHost", "api.example.com")
            });

            var models = new[]
            {
                new ApiApiModel { Endpoint = "https://$ApiHost$/a" },
                new ApiApiModel { Endpoint = "https://$ApiHost$/b" }
            };
            _resolver.ResolveEndpoints(models, "ENV1");

            Assert.AreEqual("https://api.example.com/a", models[0].EndpointResolved);
            Assert.AreEqual("https://api.example.com/b", models[1].EndpointResolved);
            _propertyValues.Received(1).GetEnvironmentProperties("ENV1", null);
        }
    }
}
