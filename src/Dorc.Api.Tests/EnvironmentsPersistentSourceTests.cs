using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using NSubstitute;
using System.Security.Principal;
using Environment = Dorc.PersistentData.Model.Environment;
using Microsoft.EntityFrameworkCore;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class EnvironmentsPersistentSourceTests
    {
        private IDeploymentContextFactory _contextFactory;
        private ISecurityObjectFilter _objectFilter;
        private IRolePrivilegesChecker _rolePrivilegesChecker;
        private IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private ILog _logger;
        private IClaimsPrincipalReader _claimsPrincipalReader;
        private IAccessControlPersistentSource _accessControlPersistentSource;
        private EnvironmentsPersistentSource _environmentsPersistentSource;
        private IPrincipal _user;
        private IDeploymentContext _context;

        [TestInitialize]
        public void Setup()
        {
            _contextFactory = Substitute.For<IDeploymentContextFactory>();
            _objectFilter = Substitute.For<ISecurityObjectFilter>();
            _rolePrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            _propertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            _logger = Substitute.For<ILog>();
            _claimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            _accessControlPersistentSource = Substitute.For<IAccessControlPersistentSource>();
            _user = Substitute.For<IPrincipal>();
            _context = Substitute.For<IDeploymentContext>();

            _environmentsPersistentSource = new EnvironmentsPersistentSource(
                _contextFactory,
                _objectFilter,
                _rolePrivilegesChecker,
                _propertyValuesPersistentSource,
                _logger,
                _claimsPrincipalReader,
                _accessControlPersistentSource
            );

            _contextFactory.GetContext().Returns(_context);
            _claimsPrincipalReader.GetUserFullDomainName(_user).Returns("testuser@domain.com");
        }

        private class TestEnvironment
        {
            public Environment Environment { get; set; }
            public List<AccessControl> AccessControls { get; set; }
            public List<Environment> Environments { get; set; }
            public DbSet<Environment> EnvironmentsDbSet { get; set; }
            public DbSet<AccessControl> AccessControlsDbSet { get; set; }
            public DbSet<EnvironmentHistory> EnvironmentHistoriesDbSet { get; set; }
        }

        private TestEnvironment SetupTestEnvironment(int envId, string oldOwnerSid, AccessLevel oldOwnerAccessLevel, string newOwnerSid = null, AccessLevel? newOwnerAccessLevel = null)
        {
            var environment = new Environment { Id = envId, ObjectId = Guid.NewGuid() };
            var accessControls = new List<AccessControl>();
            
            // Setup old owner access
            var oldOwnerAccess = new AccessControl 
            { 
                ObjectId = environment.ObjectId,
                Pid = oldOwnerSid,
                Name = "Old Owner",
                Allow = (int)oldOwnerAccessLevel
            };
            environment.AccessControls.Add(oldOwnerAccess);
            accessControls.Add(oldOwnerAccess);

            // Setup new owner access if provided
            if (newOwnerSid != null && newOwnerAccessLevel.HasValue)
            {
                var newOwnerAccess = new AccessControl 
                { 
                    ObjectId = environment.ObjectId,
                    Pid = newOwnerSid,
                    Name = "New Owner",
                    Allow = (int)newOwnerAccessLevel.Value
                };
                environment.AccessControls.Add(newOwnerAccess);
                accessControls.Add(newOwnerAccess);
            }

            var environments = new List<Environment> { environment };
            var environmentsDbSet = DbContextMock.GetQueryableMockDbSet(environments);
            var accessControlsDbSet = DbContextMock.GetQueryableMockDbSet(accessControls);
            var environmentHistoriesDbSet = DbContextMock.GetQueryableMockDbSet(new List<EnvironmentHistory>());

            _context.Environments.Returns(environmentsDbSet);
            _context.AccessControls.Returns(accessControlsDbSet);
            _context.EnvironmentHistories.Returns(environmentHistoriesDbSet);

            // Mock the Include functionality
            _accessControlPersistentSource.GetAccessControls(environment.ObjectId).Returns(accessControls.Select(ac => new AccessControlApiModel 
            { 
                Sid = ac.Sid,
                Pid = ac.Pid,
                Name = ac.Name,
                Allow = ac.Allow,
                Deny = ac.Deny
            }));

            return new TestEnvironment
            {
                Environment = environment,
                AccessControls = accessControls,
                Environments = environments,
                EnvironmentsDbSet = environmentsDbSet,
                AccessControlsDbSet = accessControlsDbSet,
                EnvironmentHistoriesDbSet = environmentHistoriesDbSet
            };
        }

        private void AssertOwnerChange(TestEnvironment testEnv, string oldOwnerSid, string newOwnerSid, AccessLevel? expectedOldOwnerAccess = null, AccessLevel? expectedNewOwnerAccess = null)
        {
            // Verify old owner's access
            var oldOwnerAccessControl = testEnv.AccessControls.FirstOrDefault(ac => ac.Pid == oldOwnerSid);
            if (expectedOldOwnerAccess.HasValue)
            {
                Assert.IsNotNull(oldOwnerAccessControl);
                Assert.AreEqual((int)expectedOldOwnerAccess.Value, oldOwnerAccessControl.Allow);
            }
            else
            {
                Assert.IsNull(oldOwnerAccessControl);
            }

            // Verify new owner's access
            var newOwnerAccessControl = testEnv.AccessControls.FirstOrDefault(ac => ac.Pid == newOwnerSid);
            if (expectedNewOwnerAccess.HasValue)
            {
                Assert.IsNotNull(newOwnerAccessControl);
                Assert.AreEqual((int)expectedNewOwnerAccess.Value, newOwnerAccessControl.Allow);
            }
            else
            {
                Assert.IsNull(newOwnerAccessControl);
            }

            // Verify owner through GetEnvironmentOwnerId
            var ownerId = _environmentsPersistentSource.GetEnvironmentOwnerId(testEnv.Environment.Id);
            Assert.AreEqual(newOwnerSid, ownerId);
        }

        [TestMethod]
        public void SetEnvironmentOwner_WithExistingOwnerHavingWriteAccess_PreservesWriteAccess()
        {
            // Arrange
            var envId = 1;
            var oldOwnerSid = "old-owner-sid";
            var newOwnerSid = "new-owner-sid";
            var newOwner = new UserElementApiModel { Username = "newowner", DisplayName = "New Owner", Pid = newOwnerSid };
            
            var testEnv = SetupTestEnvironment(envId, oldOwnerSid, AccessLevel.Owner | AccessLevel.Write);

            // Act
            var result = _environmentsPersistentSource.SetEnvironmentOwner(_user, envId, newOwner);

            // Assert
            Assert.IsTrue(result);
            AssertOwnerChange(testEnv, oldOwnerSid, newOwnerSid, AccessLevel.Write, AccessLevel.Owner);
        }

        [TestMethod]
        public void SetEnvironmentOwner_WithExistingOwnerHavingOnlyOwnerAccess_RemovesAccessControl()
        {
            // Arrange
            var envId = 1;
            var oldOwnerSid = "old-owner-sid";
            var newOwnerSid = "new-owner-sid";
            var newOwner = new UserElementApiModel { Username = "newowner", DisplayName = "New Owner", Pid = newOwnerSid };
            
            var testEnv = SetupTestEnvironment(envId, oldOwnerSid, AccessLevel.Owner);

            // Act
            var result = _environmentsPersistentSource.SetEnvironmentOwner(_user, envId, newOwner);

            // Assert
            Assert.IsTrue(result);
            AssertOwnerChange(testEnv, oldOwnerSid, newOwnerSid, null, AccessLevel.Owner);
        }

        [TestMethod]
        public void SetEnvironmentOwner_WithNewOwnerHavingExistingAccess_PreservesAndAddsOwnerAccess()
        {
            // Arrange
            var envId = 1;
            var oldOwnerSid = "old-owner-sid";
            var newOwnerSid = "new-owner-sid";
            var newOwner = new UserElementApiModel { Username = "newowner", DisplayName = "New Owner", Pid = newOwnerSid };
            
            var testEnv = SetupTestEnvironment(envId, oldOwnerSid, AccessLevel.Owner, newOwnerSid, AccessLevel.Write);

            // Act
            var result = _environmentsPersistentSource.SetEnvironmentOwner(_user, envId, newOwner);

            // Assert
            Assert.IsTrue(result);
            AssertOwnerChange(testEnv, oldOwnerSid, newOwnerSid, null, AccessLevel.Owner | AccessLevel.Write);
        }
    }
} 