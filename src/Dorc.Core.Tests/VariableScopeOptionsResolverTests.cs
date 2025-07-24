using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using NSubstitute;

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
        private IActiveDirectorySearcher _directorySearcher;
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
            _directorySearcher = Substitute.For<IActiveDirectorySearcher>();
            _logger = Substitute.For<ILog>();
            _variableResolver = Substitute.For<IVariableResolver>();

            _resolver = new VariableScopeOptionsResolver(
                _propertiesPersistentSource,
                _serversPersistentSource,
                _daemonsPersistentSource,
                _databasesPersistentSource,
                _userPermsPersistentSource,
                _environmentsPersistentSource,
                _directorySearcher,
                _logger);

            // Setup default empty returns for required calls
            _databasesPersistentSource.GetDatabasesForEnvironmentName(Arg.Any<string>()).Returns(new List<DatabaseApiModel>());
            _serversPersistentSource.GetServersForEnvId(Arg.Any<int>()).Returns(new List<ServerApiModel>());
        }

        [TestMethod]
        public void SetPropertyValues_SetsEnvironmentOwnerEmail_WhenOwnerFoundAndEmailResolved()
        {
            // Arrange
            var environment = new EnvironmentApiModel 
            { 
                EnvironmentId = 123, 
                EnvironmentName = "TestEnv",
                Details = new EnvironmentDetailsApiModel()
            };
            var ownerId = "user123";
            var ownerEmail = "test.owner@example.com";
            var userData = new UserElementApiModel { Email = ownerEmail, Pid = ownerId };

            _environmentsPersistentSource.GetEnvironmentOwnerId(123).Returns(ownerId);
            _directorySearcher.GetUserDataById(ownerId).Returns(userData);

            // Act
            _resolver.SetPropertyValues(_variableResolver, environment);

            // Assert
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwner, ownerId);
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentOwnerEmail, ownerEmail);
        }

        [TestMethod]
        public void SetPropertyValues_SetsEmptyEmail_WhenOwnerFoundButEmailNotResolved()
        {
            // Arrange
            var environment = new EnvironmentApiModel 
            { 
                EnvironmentId = 123, 
                EnvironmentName = "TestEnv",
                Details = new EnvironmentDetailsApiModel()
            };
            var ownerId = "user123";
            var userData = new UserElementApiModel { Email = null, Pid = ownerId };

            _environmentsPersistentSource.GetEnvironmentOwnerId(123).Returns(ownerId);
            _directorySearcher.GetUserDataById(ownerId).Returns(userData);

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

        [TestMethod]
        public void SetPropertyValues_SetsEmptyEmail_WhenDirectorySearcherIsNull()
        {
            // Arrange
            var environment = new EnvironmentApiModel 
            { 
                EnvironmentId = 123, 
                EnvironmentName = "TestEnv",
                Details = new EnvironmentDetailsApiModel()
            };
            var ownerId = "user123";

            var resolverWithoutDirectorySearcher = new VariableScopeOptionsResolver(
                _propertiesPersistentSource,
                _serversPersistentSource,
                _daemonsPersistentSource,
                _databasesPersistentSource,
                _userPermsPersistentSource,
                _environmentsPersistentSource,
                null, // No directory searcher
                _logger);

            _environmentsPersistentSource.GetEnvironmentOwnerId(123).Returns(ownerId);

            // Act
            resolverWithoutDirectorySearcher.SetPropertyValues(_variableResolver, environment);

            // Assert
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwner, ownerId);
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentOwnerEmail, string.Empty);
        }

        [TestMethod]
        public void SetPropertyValues_HandlesDirectorySearcherException_Gracefully()
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
            _directorySearcher.When(x => x.GetUserDataById(ownerId)).Do(x => throw new Exception("Directory service error"));

            // Act
            _resolver.SetPropertyValues(_variableResolver, environment);

            // Assert
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwner, ownerId);
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentOwnerEmail, string.Empty);
            _logger.Received(1).Error(Arg.Any<string>(), Arg.Any<Exception>());
        }

        [TestMethod]
        public void SetPropertyValues_HandlesEnvironmentsPersistentSourceException_Gracefully()
        {
            // Arrange
            var environment = new EnvironmentApiModel 
            { 
                EnvironmentId = 123, 
                EnvironmentName = "TestEnv",
                Details = new EnvironmentDetailsApiModel()
            };

            _environmentsPersistentSource.When(x => x.GetEnvironmentOwnerId(123)).Do(x => throw new Exception("Database error"));

            // Act
            _resolver.SetPropertyValues(_variableResolver, environment);

            // Assert
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwner, string.Empty);
            _variableResolver.Received(1).SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentOwnerEmail, string.Empty);
            _logger.Received(1).Error(Arg.Any<string>(), Arg.Any<Exception>());
        }
    }
}