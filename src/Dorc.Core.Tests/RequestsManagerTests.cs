using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class RequestsManagerTests
    {
        private ILog _logger = null!;
        private IProjectsPersistentSource _projectsSource = null!;
        private IComponentsPersistentSource _componentsSource = null!;
        private IEnvironmentsPersistentSource _environmentsSource = null!;
        private IRequestsManager _sut = null!;

        [TestInitialize]
        public void Setup()
        {
            _logger = Substitute.For<ILog>();
            _projectsSource = Substitute.For<IProjectsPersistentSource>();
            _componentsSource = Substitute.For<IComponentsPersistentSource>();
            _environmentsSource = Substitute.For<IEnvironmentsPersistentSource>();

            _sut = new RequestsManager(
                _logger,
                _projectsSource,
                _componentsSource,
                _environmentsSource);
        }

        [TestMethod]
        public void GetComponents_WithNullProjectId_ReturnsEmptyList()
        {
            // Act
            var result = _sut.GetComponents(null, null);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count());
        }

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Act
            var manager = new RequestsManager(
                _logger,
                _projectsSource,
                _componentsSource,
                _environmentsSource);

            // Assert
            Assert.IsNotNull(manager);
        }

        [TestMethod]
        public void GetComponents_HandlesExceptionGracefully()
        {
            // Arrange
            var projectId = 1;
            _projectsSource.GetComponentsForProject(projectId).Returns(x => throw new Exception("Test exception"));

            // Act & Assert
            Assert.ThrowsException<Exception>(() => _sut.GetComponents(projectId, null));
            _logger.Received().Error(Arg.Any<string>(), Arg.Any<Exception>());
        }
    }
}
