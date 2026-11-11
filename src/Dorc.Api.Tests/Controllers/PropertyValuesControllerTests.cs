using Dorc.Api.Controllers;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class PropertyValuesControllerTests
    {
        private IPropertyValuesService _propertyValuesService = null!;
        private IPropertyValuesPersistentSource _propertyValuesPersistentSource = null!;
        private IVariableResolver _variableResolver = null!;
        private IEnvironmentsPersistentSource _environmentsPersistentSource = null!;
        private IVariableScopeOptionsResolver _variableScopeOptionsResolver = null!;
        private IDirectorySearcherFactory _directorySearcherFactory = null!;
        private IActiveDirectorySearcher _directorySearcher = null!;
        private ILogger<PropertyValuesController> _logger = null!;
        private PropertyValuesController _controller = null!;

        [TestInitialize]
        public void Setup()
        {
            _propertyValuesService = Substitute.For<IPropertyValuesService>();
            _propertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            _variableResolver = Substitute.For<IVariableResolver>();
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _variableScopeOptionsResolver = Substitute.For<IVariableScopeOptionsResolver>();
            _directorySearcherFactory = Substitute.For<IDirectorySearcherFactory>();
            _directorySearcher = Substitute.For<IActiveDirectorySearcher>();
            _directorySearcherFactory.GetEntraSearcher().Returns(_directorySearcher);
            _logger = Substitute.For<ILogger<PropertyValuesController>>();

            _controller = new PropertyValuesController(
                _propertyValuesService,
                _propertyValuesPersistentSource,
                _variableResolver,
                _environmentsPersistentSource,
                _variableScopeOptionsResolver,
                _directorySearcherFactory,
                _logger)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.Name, "TestUser")
                        }))
                    }
                }
            };
        }

        [TestMethod]
        public void GetScopeOptions_WhenNormalizedTagCollisionThrowsInvalidOperationException_ReturnsBadRequestWithMessage()
        {
            // Arrange
            const string envName = "Endur DV 10";
            const string collisionMessage =
                "Tags 'A B' and 'A_B' both produce deployment variable 'ServerNames_A_B'. Rename one tag before deployment.";
            var environment = new EnvironmentApiModel
            {
                EnvironmentId = 42,
                EnvironmentName = envName,
                Details = new EnvironmentDetailsApiModel()
            };
            _environmentsPersistentSource.GetEnvironment(envName, Arg.Any<ClaimsPrincipal>())
                .Returns(environment);
            _environmentsPersistentSource.GetEnvironmentOwnerIds(42)
                .Returns(new List<string>());
            _variableScopeOptionsResolver
                .When(r => r.SetPropertyValues(_variableResolver, environment))
                .Do(_ => throw new InvalidOperationException(collisionMessage));

            // Act
            var result = _controller.GetScopeOptions(envName);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            var badRequest = (BadRequestObjectResult)result;
            Assert.AreEqual(StatusCodes.Status400BadRequest, badRequest.StatusCode);
            Assert.AreEqual(collisionMessage, badRequest.Value);
        }

        [TestMethod]
        public void GetScopeOptions_WhenValid_ReturnsOkWithScopeOptions()
        {
            // Arrange
            const string envName = "Endur DV 10";
            var environment = new EnvironmentApiModel
            {
                EnvironmentId = 42,
                EnvironmentName = envName,
                Details = new EnvironmentDetailsApiModel()
            };
            _environmentsPersistentSource.GetEnvironment(envName, Arg.Any<ClaimsPrincipal>())
                .Returns(environment);
            _environmentsPersistentSource.GetEnvironmentOwnerIds(42)
                .Returns(new List<string>());
            _variableResolver.LocalProperties().Returns(new Dictionary<string, VariableValue>
            {
                ["ServerNames_A_B"] = new VariableValue { Value = "GMDV-ENDAPP30", Type = typeof(string) }
            });

            // Act
            var result = _controller.GetScopeOptions(envName);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = (OkObjectResult)result;
            var options = okResult.Value as List<PropertyValueScopeOptionApiModel>;
            Assert.IsNotNull(options);
            Assert.AreEqual(1, options.Count);
            Assert.AreEqual("$ServerNames_A_B$", options[0].ValueOption);
            Assert.AreEqual("GMDV-ENDAPP30", options[0].SampleResolvedValue);
        }
    }
}
