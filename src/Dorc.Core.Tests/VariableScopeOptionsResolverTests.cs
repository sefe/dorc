using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using NSubstitute;
using System;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class VariableScopeOptionsResolverTests
    {
        private IPropertiesPersistentSource _propertiesPersistentSource;
        private IServersPersistentSource _serversPersistentSource;
        private IDaemonsPersistentSource _daemonsPersistentSource;
        private IDatabasesPersistentSource _databasesPersistentSource;
        private IUserPermsPersistentSource _userPermsPersistentSource;
        private IEnvironmentsPersistentSource _environmentsPersistentSource;
        private ILog _logger;
        private IVariableResolver _variableResolver;
        private VariableScopeOptionsResolver _resolver;

        [TestInitialize]
        public void Setup()
        {
            _propertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            _serversPersistentSource = Substitute.For<IServersPersistentSource>();
            _daemonsPersistentSource = Substitute.For<IDaemonsPersistentSource>();
            _databasesPersistentSource = Substitute.For<IDatabasesPersistentSource>();
            _userPermsPersistentSource = Substitute.For<IUserPermsPersistentSource>();
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _logger = Substitute.For<ILog>();
            _variableResolver = Substitute.For<IVariableResolver>();

            _resolver = new VariableScopeOptionsResolver(
                _propertiesPersistentSource,
                _serversPersistentSource,
                _daemonsPersistentSource,
                _databasesPersistentSource,
                _userPermsPersistentSource,
                _environmentsPersistentSource,
                _logger);

            // Setup default empty returns for required calls
            _databasesPersistentSource.GetDatabasesForEnvironmentName(Arg.Any<string>()).Returns(new List<DatabaseApiModel>());
            _serversPersistentSource.GetServersForEnvId(Arg.Any<int>()).Returns(new List<ServerApiModel>());
        }

        [TestMethod]
        public void SetPropertyValues_WithDeploymentRequest_SetsEnvironmentOwnerEmailFromRequest()
        {
            // Arrange
            var environment = new EnvironmentApiModel 
            { 
                EnvironmentId = 123, 
                EnvironmentName = "TestEnv",
                Details = new EnvironmentDetailsApiModel()
            };
            var deploymentRequest = new DeploymentRequestApiModel
            {
                EnvironmentOwnerEmail = "test.owner@example.com"
            };
            var ownerId = "user123";

            _environmentsPersistentSource.GetEnvironmentOwnerId(123).Returns(ownerId);

            // Act
            _resolver.SetPropertyValues(_variableResolver, environment, deploymentRequest);

            // Assert
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwner, ownerId);
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentOwnerEmail, "test.owner@example.com");
        }

        [TestMethod]
        public void SetPropertyValues_WithDeploymentRequestButNoEmail_SetsEmptyEmail()
        {
            // Arrange
            var environment = new EnvironmentApiModel 
            { 
                EnvironmentId = 123, 
                EnvironmentName = "TestEnv",
                Details = new EnvironmentDetailsApiModel()
            };
            var deploymentRequest = new DeploymentRequestApiModel
            {
                EnvironmentOwnerEmail = null
            };
            var ownerId = "user123";

            _environmentsPersistentSource.GetEnvironmentOwnerId(123).Returns(ownerId);

            // Act
            _resolver.SetPropertyValues(_variableResolver, environment, deploymentRequest);

            // Assert
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwner, ownerId);
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentOwnerEmail, string.Empty);
        }

        [TestMethod]
        public void SetPropertyValues_WithoutDeploymentRequest_SetsEmptyEmail()
        {
            // Arrange
            var environment = new EnvironmentApiModel 
            { 
                EnvironmentId = 123, 
                EnvironmentName = "TestEnv",
                Details = new EnvironmentDetailsApiModel()
            };
            var ownerId = "user123";

            _environmentsPersistentSource.GetEnvironmentOwnerId(123).Returns(ownerId);

            // Act
            _resolver.SetPropertyValues(_variableResolver, environment);

            // Assert
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwner, ownerId);
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentOwnerEmail, string.Empty);
        }

        [TestMethod]
        public void SetPropertyValues_SetsEmptyValues_WhenNoOwnerFound()
        {
            // Arrange
            var environment = new EnvironmentApiModel 
            { 
                EnvironmentId = 123, 
                EnvironmentName = "TestEnv",
                Details = new EnvironmentDetailsApiModel()
            };

            _environmentsPersistentSource.GetEnvironmentOwnerId(123).Returns(string.Empty);

            // Act
            _resolver.SetPropertyValues(_variableResolver, environment);

            // Assert
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwner, string.Empty);
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentOwnerEmail, string.Empty);
        }
    }
}