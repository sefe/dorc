using System.Security.Claims;
using System.Security.Principal;
using Dorc.Api.Properties;
using Dorc.Api.Services;
using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using NSubstitute;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class ScopedPropertyValueEditorTests
    {
        [TestMethod]
        public void GetPropertyValuesTestByPropertyName()
        {
            const string propertyName = @"Test property";
            const string environmentName = @"Test environment";
            const string propertyValue = @"Test property value";
            const bool secure = false;

            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            mockedClaimsPrincipalReader.GetSidsForUser(Arg.Any<IPrincipal>()).Returns(new List<string>());

            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());

            mockedPropertyValuesPersistentSource.GetPropertyValuesForUser(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = propertyName, Secure = secure}, PropertyValueFilter = environmentName, Value = propertyValue
                    }
                });

            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            var result = testService.GetPropertyValues(propertyName, null, new GenericPrincipal(new GenericIdentity("test-user"), null)).FirstOrDefault();
            if (result.Property.Name != propertyName ||
                result.Property.Secure != secure ||
                result.Value != propertyValue ||
                result.PropertyValueFilter != environmentName)
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void GetPropertyValuesTestByEnvironmentNameUnsecure()
        {
            const string propertyName = @"Test property";
            const string environmentName = @"Test environment";
            const string propertyValue = @"Test property value";
            const bool secure = false;

            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            mockedClaimsPrincipalReader.GetSidsForUser(Arg.Any<IPrincipal>()).Returns(new List<string>());

            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());

            mockedPropertyValuesPersistentSource.GetPropertyValuesForUser(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = propertyName, Secure = secure}, PropertyValueFilter = environmentName, Value = propertyValue
                    }
                });

            var result = testService.GetPropertyValues(propertyName, environmentName, new GenericPrincipal(new GenericIdentity("test-user"), null)).FirstOrDefault();
            if (result.Property.Name != propertyName ||
                result.Property.Secure != secure ||
                result.Value != propertyValue ||
                result.PropertyValueFilter != environmentName)
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void GetPropertyValuesTestByEnvironmentNameSecure()
        {
            const string propertyName = @"Test property";
            const string environmentName = @"Test environment";
            const string propertyValue = @"Test property value";
            const bool secure = true;

            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            mockedClaimsPrincipalReader.GetSidsForUser(Arg.Any<IPrincipal>()).Returns(new List<string>());

            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());

            mockedPropertyValuesPersistentSource.GetPropertyValuesForUser(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = propertyName, Secure = secure}, PropertyValueFilter = environmentName, Value = propertyValue
                    }
                });

            Assert.Throws<NonEnoughRightsException>(() => testService.GetPropertyValues(propertyName, environmentName, new GenericPrincipal(new GenericIdentity("test-user"), null)));
        }

        [TestMethod]
        public void GetPropertyValuesTestByEnvironmentNameMixed()
        {
            const string environmentName = @"Test environment";

            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            mockedClaimsPrincipalReader.GetSidsForUser(Arg.Any<IPrincipal>()).Returns(new List<string>());

            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());

            mockedPropertyValuesPersistentSource.GetEnvironmentProperties(Arg.Any<string>(), Arg.Any<string>())
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = "property1", Secure = true}, PropertyValueFilter = environmentName, Value = "Value1"
                    },
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = "property2", Secure = true}, PropertyValueFilter = environmentName, Value = "Value2"
                    },
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = "property3", Secure = false}, PropertyValueFilter = environmentName, Value = "Value3"
                    },
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = "property4", Secure = false}, PropertyValueFilter = environmentName, Value = "Value4"
                    }
                });

            var result = testService.GetPropertyValues(
                propertyName: String.Empty,
                environmentName: environmentName,
                user: new GenericPrincipal(new GenericIdentity("test-user"), null)
                );
            Assert.IsTrue(result.Where(property => property.Property.Secure).All(property => String.IsNullOrEmpty(property.Value)));
            Assert.IsTrue(result.Where(property => !property.Property.Secure).All(property => !String.IsNullOrEmpty(property.Value)));
        }

        [TestMethod]
        public void GetPropertyValuesTestByPropertyNameAndEnvironmentName()
        {
            const string propertyName = @"Test property";
            const string environmentName = @"Test environment";
            const string propertyValue = @"Test property value";
            const bool secure = false;

            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();

            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());

            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            mockedPropertyValuesPersistentSource.GetPropertyValuesForUser(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = propertyName, Secure = secure}, PropertyValueFilter = environmentName, Value = propertyValue
                    }
                });

            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            mockedClaimsPrincipalReader.GetSidsForUser(Arg.Any<IPrincipal>()).Returns(new List<string>());

            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            var result = testService.GetPropertyValues(propertyName, environmentName, new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (result.FirstOrDefault().Property.Name != propertyName ||
                result.FirstOrDefault().Property.Secure != secure ||
                result.FirstOrDefault().Value != propertyValue ||
                result.FirstOrDefault().PropertyValueFilter != environmentName || result.Count() != 1)
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void DeletePropertyValuesTestPermissions()
        {
            const string propertyName = @"Test property";
            const string environmentName = @"Test environment";
            const string propertyValue = @"Test property value";
            const bool secure = false;

            var testProperty = new PropertyValueDto
            {
                Property = new PropertyApiModel { Secure = secure, Name = propertyName },
                PropertyValueFilter = environmentName,
                Value = propertyValue
            };

            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            mockedPropertyValuesPersistentSource.GetPropertyValuesByName(Arg.Any<string>())
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = propertyName, Secure = secure}, PropertyValueFilter = environmentName, Value = propertyValue
                    }
                });

            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            mockedApiSecurityService
                .CanModifyPropertyValue(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(false);

            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockClaimsPrincipalReader);

            var result = testService.DeletePropertyValues(new List<PropertyValueDto> { testProperty },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (result.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void DeletePropertyValuesTestIfPropertyValueNotExist()
        {
            const string propertyName = @"Test property";
            const string environmentName = @"Test environment";
            const string propertyValue = @"Test property value";
            const bool secure = false;

            var testProperty = new PropertyValueDto
            {
                Property = new PropertyApiModel { Secure = secure, Name = propertyName },
                PropertyValueFilter = environmentName,
                Value = propertyValue
            };

            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();

            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            mockedPropertyValuesPersistentSource.GetPropertyValuesByName(Arg.Any<string>())
                .Returns(new PropertyValueDto[] { });

            mockedApiSecurityService
                .CanModifyPropertyValue(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(true);

            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            var result = testService.DeletePropertyValues(new List<PropertyValueDto> { testProperty },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (result.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void DeletePropertyValuesTestDeleteEnvironmentProperty()
        {
            const string propertyName = @"Test property";
            const string environmentName = @"Test environment";
            const string propertyValue = @"Test property value";
            const bool secure = false;
            const int propertyId = 1;
            const int propertyValueId = 2;
            const int propertyValueFilterId = 3;

            var property = new PropertyValueDto
            {
                Property = new PropertyApiModel { Secure = secure, Name = propertyName },
                PropertyValueFilter = environmentName,
                Value = propertyValue
            };

            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            mockedPropertyValuesPersistentSource.GetPropertyValuesByName(Arg.Any<string>())
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = propertyName, Secure = secure}, PropertyValueFilter = environmentName, Value = propertyValue,
                        Id = propertyValueId, PropertyValueFilterId = propertyValueFilterId
                    }
                });
            mockedPropertyValuesPersistentSource.Remove(Arg.Any<long?>())
                .Returns(true);

            mockedAuditsPersistentSource.When(m => m.AddRecord(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()));
            mockedPropertiesPersistentSource.GetProperty(Arg.Any<string>())
                .Returns(new PropertyApiModel { Name = propertyName, Secure = secure, Id = propertyId });

            mockedApiSecurityService
                .CanModifyPropertyValue(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(true);

            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            var result = testService.DeletePropertyValues(new List<PropertyValueDto> { property },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (!result.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }

            mockedPropertyValuesPersistentSource.When(m => m.Remove(Arg.Any<long?>())).Throws(e => new ArgumentException());
            var resultErr = testService.DeletePropertyValues(new List<PropertyValueDto> { property },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (resultErr.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }

            mockedPropertyValuesPersistentSource.Remove(Arg.Any<long?>());
            var resultFalse = testService.DeletePropertyValues(new List<PropertyValueDto> { property },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (resultFalse.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void DeletePropertyValuesTestDeleteDefaultProperty()
        {
            const string propertyName = @"Test property";
            const string propertyValue = @"Test property value";
            const bool secure = false;
            const int propertyId = 1;
            const int propertyValueId = 2;
            const int propertyValueFilterId = 3;

            var testProperty = new PropertyValueDto
            { Property = new PropertyApiModel { Secure = secure, Name = propertyName }, Value = propertyValue };

            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            mockedPropertyValuesPersistentSource.GetPropertyValuesByName(Arg.Any<string>())
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = propertyName, Secure = secure}, Value = propertyValue,
                        Id = propertyValueId, PropertyValueFilterId = propertyValueFilterId
                    }
                });
            mockedPropertyValuesPersistentSource.Remove(Arg.Any<long?>())
                .Returns(true);

            mockedAuditsPersistentSource.AddRecord(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
            mockedPropertiesPersistentSource.GetProperty(Arg.Any<string>())
                .Returns(new PropertyApiModel { Name = propertyName, Secure = secure, Id = propertyId });

            mockedApiSecurityService
                .CanModifyPropertyValue(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(true);

            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            var result = testService.DeletePropertyValues(new List<PropertyValueDto> { testProperty },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (!result.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }

            mockedPropertyValuesPersistentSource.Remove(Arg.Any<long?>())
                .Returns(false);
            IEnumerable<Response> result1 = testService.DeletePropertyValues(new List<PropertyValueDto> { testProperty },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            var first = result1.FirstOrDefault();
            if (first != null && first.Status.Equals("success"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void PostPropertyValuesTestCreateIfEnvironmentNotExists()
        {
            const string propertyName = @"Test property";
            const string environmentName = @"Test environment";
            const string propertyValue = @"Test property value";
            const bool secure = false;

            var testProperty = new PropertyValueDto
            {
                Property = new PropertyApiModel { Secure = secure, Name = propertyName },
                PropertyValueFilter = environmentName,
                Value = propertyValue
            };
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();

            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());


            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (result.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void PostPropertyValuesTestEnvironmentPermissions()
        {
            const string propertyName = @"Test property";
            const string environmentName = @"Test environment";
            const string propertyValue = @"Test property value";
            const bool secure = false;

            var testProperty = new PropertyValueDto
            {
                Property = new PropertyApiModel { Secure = secure, Name = propertyName },
                PropertyValueFilter = environmentName,
                Value = propertyValue
            };

            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());


            mockedApiSecurityService
                .CanModifyPropertyValue(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(false);

            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (result.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void PostPropertyValuesTestCreateEnvironmentPropertyValue()
        {
            const string propertyName = @"Test property";
            const string environmentName = @"Test environment";
            const string propertyValue = @"Test property value";
            const string decryptedValue = @"Decrypted Value";
            const bool secure = false;
            const int propertyId = 1;
            const int propertyValueId = 2;

            var testProperty = new PropertyValueDto
            {
                Property = new PropertyApiModel { Secure = secure, Name = propertyName },
                PropertyValueFilter = environmentName,
                Value = propertyValue
            };

            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>(), Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel());

            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            mockedApiSecurityService
                .CanModifyPropertyValue(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(true);
            mockedPropertyEncryptor.DecryptValue(Arg.Any<string>())
                .Returns(decryptedValue);
            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();

            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            mockedPropertyValuesPersistentSource.GetPropertyValuesByName(Arg.Any<string>())
                .Returns(new PropertyValueDto[] { });
            mockedPropertyValuesPersistentSource.Remove(Arg.Any<long?>())
                .Returns(true);
            mockedPropertyValuesPersistentSource.AddPropertyValue(Arg.Any<PropertyValueDto>())
                .Returns(new PropertyValueDto { Id = propertyValueId, Property = new PropertyApiModel { Name = propertyName }, Value = propertyValue });

            mockedAuditsPersistentSource.AddRecord(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
            mockedPropertiesPersistentSource.GetProperty(Arg.Any<string>())
                .Returns(new PropertyApiModel { Name = propertyName, Secure = secure, Id = propertyId });

            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (!result.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void PostPropertyValuesTestCreateDefaultPropertyValue()
        {
            const string propertyName = @"Test property";
            const string environmentName = null;
            const string propertyValue = @"Test property value";
            const string decryptedValue = @"Decrypted Value";
            const bool secure = false;
            const int propertyId = 1;
            const int propertyValueId = 2;

            var testProperty = new PropertyValueDto
            {
                Property = new PropertyApiModel { Secure = secure, Name = propertyName },
                PropertyValueFilter = environmentName,
                Value = propertyValue
            };

            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());

            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();

            mockedApiSecurityService
                .CanModifyPropertyValue(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(true);
            mockedPropertyEncryptor.DecryptValue(Arg.Any<string>())
                .Returns(decryptedValue);
            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();

            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            mockedPropertyValuesPersistentSource.GetPropertyValuesByName(Arg.Any<string>())
                .Returns(new PropertyValueDto[] { });
            mockedPropertyValuesPersistentSource.Remove(Arg.Any<long?>())
                .Returns(true);
            mockedPropertyValuesPersistentSource.AddPropertyValue(Arg.Any<PropertyValueDto>())
                .Returns(new PropertyValueDto { Id = propertyValueId, Property = new PropertyApiModel { Name = propertyName }, Value = propertyValue });

            mockedAuditsPersistentSource.AddRecord(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
            mockedPropertiesPersistentSource.GetProperty(Arg.Any<string>())
                .Returns(new PropertyApiModel { Name = propertyName, Secure = secure, Id = propertyId });
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (!result.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void PostPropertyValuesTestTryCreateIfAlreadyExists()
        {
            const string propertyName = @"Test property";
            const string environmentName = @"Test environment";
            const string propertyValue = @"Test property value";
            const string decryptedValue = @"Decrypted Value";
            const bool secure = true;

            var testProperty = new PropertyValueDto
            {
                Property = new PropertyApiModel { Secure = secure, Name = propertyName },
                PropertyValueFilter = environmentName,
                Value = propertyValue
            };

            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();

            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());

            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            mockedApiSecurityService
                .CanModifyPropertyValue(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(true);
            mockedPropertyEncryptor.DecryptValue(Arg.Any<string>())
                .Returns(decryptedValue);

            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();

            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (result.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void PutPropertyValuesTestEnvironmentPermissions()
        {
            const string propertyName = @"Test property";
            const string environmentName = @"Test environment";
            const string propertyValue = @"Test property value";
            const bool secure = false;

            var testProperty = new PropertyValueDto
            {
                Property = new PropertyApiModel { Secure = secure, Name = propertyName },
                PropertyValueFilter = environmentName,
                Value = propertyValue
            };

            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();

            mockedApiSecurityService
                .CanModifyPropertyValue(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(false);
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            var result = testService.PutPropertyValues(new List<PropertyValueDto> { testProperty },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (!result.FirstOrDefault().Status.StartsWith("error") || !result.FirstOrDefault().Status.Contains("permissions"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void PutPropertyValuesTestIfNotExists()
        {
            const string propertyName = @"Test property";
            const string environmentName = @"Test environment";
            const string propertyValue = @"Test property value";
            const string decryptedValue = @"Decrypted Value";
            const bool secure = true;

            var testProperty = new PropertyValueDto
            {
                Property = new PropertyApiModel { Secure = secure, Name = propertyName },
                PropertyValueFilter = environmentName,
                Value = propertyValue
            };


            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();

            mockedApiSecurityService
                .CanModifyPropertyValue(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(true);
            mockedPropertyEncryptor.DecryptValue(Arg.Any<string>())
                .Returns(decryptedValue);

            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            var result = testService.PutPropertyValues(new List<PropertyValueDto> { testProperty },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (result.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void PutPropertyValuesTestUpdateDefaultPropertyValue()
        {
            const string propertyName = @"Test property";
            const string environmentName = null;
            const string propertyValue = @"Test property value";
            const string decryptedValue = @"Decrypted Value";
            const bool secure = false;
            const int propertyId = 1;
            const int propertyValueId = 2;

            var testProperty = new PropertyValueDto
            {
                Property = new PropertyApiModel { Secure = secure, Name = propertyName },
                PropertyValueFilter = environmentName,
                Value = propertyValue
            };


            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();

            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());

            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            mockedApiSecurityService
                .CanModifyPropertyValue(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(true);
            mockedPropertyEncryptor.DecryptValue(Arg.Any<string>())
                .Returns(decryptedValue);

            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();

            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            mockedPropertyValuesPersistentSource.GetPropertyValuesByName(Arg.Any<string>())
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = propertyName , Secure = secure}, Value = "Old Property Value",
                        Id = propertyValueId, PropertyValueFilter = environmentName
                    }
                });
            mockedPropertyValuesPersistentSource.Remove(Arg.Any<long?>())
                .Returns(true);
            mockedPropertyValuesPersistentSource.AddPropertyValue(Arg.Any<PropertyValueDto>())
                .Returns(new PropertyValueDto { Id = propertyValueId, Property = new PropertyApiModel { Name = propertyName }, Value = propertyValue });

            mockedAuditsPersistentSource.AddRecord(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
            mockedPropertiesPersistentSource.GetProperty(Arg.Any<string>())
                .Returns(new PropertyApiModel { Name = propertyName, Secure = secure, Id = propertyId });
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            var result = testService.PutPropertyValues(new List<PropertyValueDto> { testProperty },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (!result.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void PutPropertyValuesTestUpdateEnvironmentPropertyValue()
        {
            const string propertyName = @"Test property";
            const string environmentName = @"Test environment name";
            const string propertyValue = @"Test property value";
            const string decryptedValue = @"Decrypted Value";
            const bool secure = false;
            const int propertyId = 1;
            const int propertyValueId = 2;
            const int propertyValueFilterId = 3;

            var testProperty = new PropertyValueDto
            {
                Property = new PropertyApiModel { Secure = secure, Name = propertyName },
                PropertyValueFilter = environmentName,
                Value = propertyValue
            };


            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());

            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();

            mockedApiSecurityService
                .CanModifyPropertyValue(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(true);
            mockedPropertyEncryptor.DecryptValue(Arg.Any<string>())
                .Returns(decryptedValue);

            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();

            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            mockedPropertyValuesPersistentSource.GetPropertyValuesByName(Arg.Any<string>())
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = propertyName , Secure = secure}, Value = "Old Property Value",
                        Id = propertyValueId, PropertyValueFilterId = propertyValueFilterId, PropertyValueFilter = environmentName
                    }
                });
            mockedPropertyValuesPersistentSource.Remove(Arg.Any<long?>())
                .Returns(true);
            mockedPropertyValuesPersistentSource.AddPropertyValue(Arg.Any<PropertyValueDto>())
                .Returns(new PropertyValueDto { Id = propertyValueId, Property = new PropertyApiModel { Name = propertyName }, Value = propertyValue });

            mockedAuditsPersistentSource.AddRecord(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
            mockedPropertiesPersistentSource.GetProperty(Arg.Any<string>())
                .Returns(new PropertyApiModel { Name = propertyName, Secure = secure, Id = propertyId });
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new ScopedPropertyValueEditor(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            var result = testService.PutPropertyValues(new List<PropertyValueDto> { testProperty },
                new GenericPrincipal(new GenericIdentity("test-user"), null));
            if (!result.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void GetPropertyValues_InheritedSecureValuesAndPowerUserGlobalEditable_CoverAllBranches()
        {
            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            mockedClaimsPrincipalReader.GetSidsForUser(Arg.Any<IPrincipal>()).Returns(new List<string> { "sid-1" });

            mockedEnvironmentsPersistentSource.GetEnvironment("ChildEnv", Arg.Any<IPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentName = "ChildEnv" });

            mockedPropertyValuesPersistentSource
                .GetPropertyValuesForUser(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(_ => new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = "ChildSecret", Secure = true },
                        PropertyValueFilter = "ChildEnv",
                        Value = "enc-child"
                    },
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = "ParentDeniedSecret", Secure = true },
                        PropertyValueFilter = "ParentDeniedEnv",
                        Value = "enc-parent-denied"
                    },
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = "ParentAllowedSecret", Secure = true },
                        PropertyValueFilter = "ParentAllowedEnv",
                        Value = "enc-parent-allowed"
                    },
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = "GlobalSecret", Secure = true },
                        PropertyValueFilter = null,
                        Value = "enc-global",
                        UserEditable = false
                    }
                });

            mockedApiSecurityService.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), "ChildEnv").Returns(true);
            mockedApiSecurityService.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), "ParentDeniedEnv").Returns(false);
            mockedApiSecurityService.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), "ParentAllowedEnv").Returns(true);

            mockedPropertyEncryptor.DecryptValue("enc-child").Returns("dec-child");
            mockedPropertyEncryptor.DecryptValue("enc-parent-allowed").Returns("dec-parent-allowed");
            mockedPropertyEncryptor.DecryptValue("enc-global").Returns("dec-global");

            mockedPrivilegesChecker.IsAdmin(Arg.Any<ClaimsPrincipal>()).Returns(false);
            mockedPrivilegesChecker.IsPowerUser(Arg.Any<ClaimsPrincipal>()).Returns(true);

            var testService = new ScopedPropertyValueEditor(
                mockedApiSecurityService,
                mockedPropertyEncryptor,
                mockedPropertiesPersistentSource,
                mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource,
                mockedAuditsPersistentSource,
                mockedPrivilegesChecker,
                mockedLog,
                mockedClaimsPrincipalReader);

            var user = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));
            var results = testService.GetPropertyValues(null, "ChildEnv", user).ToList();

            Assert.AreEqual("dec-child", results[0].Value);
            Assert.AreEqual(string.Empty, results[1].Value); // Masked because user lacks ReadSecrets on ParentDeniedEnv
            Assert.AreEqual("dec-parent-allowed", results[2].Value);
            Assert.AreEqual("dec-global", results[3].Value);

            // PowerUser gets UserEditable = true on global (null/empty filter), but not on scoped filter
            Assert.IsFalse(results[0].UserEditable);
            Assert.IsTrue(results[3].UserEditable);

            // Listing all properties for an environment when ALL properties are Secure and caller lacks ReadSecrets returns masked values without throwing
            mockedApiSecurityService.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), "ChildEnv").Returns(false);
            var allSecureWithoutReadSecrets = testService.GetPropertyValues(string.Empty, "ChildEnv", user).ToList();
            Assert.HasCount(4, allSecureWithoutReadSecrets);
            Assert.IsTrue(allSecureWithoutReadSecrets.All(p => p.Value == string.Empty));

            // Querying by propertyName without environmentName checks CanReadSecrets per PropertyValueFilter
            var byPropWithoutEnv = testService.GetPropertyValues("ChildSecret", null, user).ToList();
            Assert.AreEqual(string.Empty, byPropWithoutEnv[0].Value); // ChildEnv -> CanReadSecrets false
            Assert.AreEqual(string.Empty, byPropWithoutEnv[1].Value); // ParentDeniedEnv -> CanReadSecrets false
            Assert.AreEqual("dec-parent-allowed", byPropWithoutEnv[2].Value); // ParentAllowedEnv -> CanReadSecrets true
            Assert.AreEqual(string.Empty, byPropWithoutEnv[3].Value); // Global -> CanReadSecrets(null) false

            // Admin gets UserEditable = true on all properties
            mockedPrivilegesChecker.IsAdmin(Arg.Any<ClaimsPrincipal>()).Returns(true);
            var adminResults = testService.GetPropertyValues(null, "ChildEnv", user).ToList();
            Assert.IsTrue(adminResults.All(p => p.UserEditable));
        }

        [TestMethod]
        public void PostAndPutPropertyValues_WriteWithoutReadSecrets_MasksReturnedSecretValueUnlessCanReadSecrets()
        {
            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockedLog = new MockedLog<ScopedPropertyValueEditor>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var user = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));
            mockedEnvironmentsPersistentSource.GetEnvironment("DevEnv", user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "DevEnv", UserEditable = true });
            mockedEnvironmentsPersistentSource.GetEnvironment("DevEnv")
                .Returns(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "DevEnv", UserEditable = true });

            mockedApiSecurityService.CanModifyPropertyValue(user, "DevEnv").Returns(true);
            mockedPropertiesPersistentSource.GetProperty("SecretProp")
                .Returns(new PropertyApiModel { Id = 1, Name = "SecretProp", Secure = true });
            mockedPropertyEncryptor.DecryptValue(Arg.Any<string>()).Returns("decrypted-secret");

            var testService = new ScopedPropertyValueEditor(
                mockedApiSecurityService,
                mockedPropertyEncryptor,
                mockedPropertiesPersistentSource,
                mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource,
                mockedAuditsPersistentSource,
                mockedPrivilegesChecker,
                mockedLog,
                mockedClaimsPrincipalReader);

            // 1. POST without ReadSecrets -> succeeds, but returned Item.Value is masked to string.Empty
            mockedApiSecurityService.CanReadSecrets(user, "DevEnv").Returns(false);
            mockedPropertyValuesPersistentSource.GetPropertyValuesByName("SecretProp")
                .Returns(Array.Empty<PropertyValueDto>());
            mockedPropertyValuesPersistentSource.AddPropertyValue(Arg.Any<PropertyValueDto>())
                .Returns(new PropertyValueDto
                {
                    Id = 101,
                    Property = new PropertyApiModel { Id = 1, Name = "SecretProp", Secure = true },
                    PropertyValueFilter = "DevEnv",
                    Value = "cipher-text"
                });

            var postNoRead = testService.PostPropertyValues(new List<PropertyValueDto>
            {
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "SecretProp", Secure = true },
                    PropertyValueFilter = "DevEnv",
                    Value = "super-secret"
                }
            }, user).Single();
            Assert.AreEqual("success", postNoRead.Status);
            Assert.AreEqual(string.Empty, ((PropertyValueDto)postNoRead.Item).Value);

            // 2. POST with ReadSecrets -> succeeds and preserves unmasked value
            mockedApiSecurityService.CanReadSecrets(user, "DevEnv").Returns(true);
            var postWithRead = testService.PostPropertyValues(new List<PropertyValueDto>
            {
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "SecretProp", Secure = true },
                    PropertyValueFilter = "DevEnv",
                    Value = "super-secret"
                }
            }, user).Single();
            Assert.AreEqual("success", postWithRead.Status);
            Assert.AreEqual("super-secret", ((PropertyValueDto)postWithRead.Item).Value);

            // 3. PUT without ReadSecrets -> succeeds, but returned Item.Value is masked to string.Empty
            mockedApiSecurityService.CanReadSecrets(user, "DevEnv").Returns(false);
            mockedPropertyValuesPersistentSource.GetPropertyValuesByName("SecretProp")
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Id = 101,
                        Property = new PropertyApiModel { Id = 1, Name = "SecretProp", Secure = true },
                        PropertyValueFilter = "DevEnv",
                        PropertyValueFilterId = 55,
                        Value = "old-cipher"
                    }
                });
            mockedPropertyValuesPersistentSource.UpdatePropertyValue(101, "updated-secret")
                .Returns(new PropertyValueDto
                {
                    Id = 101,
                    Property = new PropertyApiModel { Id = 1, Name = "SecretProp", Secure = true },
                    PropertyValueFilter = "DevEnv",
                    Value = "new-cipher"
                });

            var putNoRead = testService.PutPropertyValues(new List<PropertyValueDto>
            {
                new PropertyValueDto
                {
                    Id = 101,
                    Property = new PropertyApiModel { Name = "SecretProp", Secure = true },
                    PropertyValueFilter = "DevEnv",
                    Value = "updated-secret"
                }
            }, user).Single();
            Assert.AreEqual("success", putNoRead.Status);
            Assert.AreEqual(string.Empty, ((PropertyValueDto)putNoRead.Item).Value);

            // 4. PUT with ReadSecrets -> succeeds and preserves unmasked value
            mockedApiSecurityService.CanReadSecrets(user, "DevEnv").Returns(true);
            var putWithRead = testService.PutPropertyValues(new List<PropertyValueDto>
            {
                new PropertyValueDto
                {
                    Id = 101,
                    Property = new PropertyApiModel { Name = "SecretProp", Secure = true },
                    PropertyValueFilter = "DevEnv",
                    Value = "updated-secret"
                }
            }, user).Single();
            Assert.AreEqual("success", putWithRead.Status);
            Assert.AreEqual("updated-secret", ((PropertyValueDto)putWithRead.Item).Value);

            // 5. Idempotent PUT (unchanged value) with a randomized encryptor skips DB/audit writes, returns Status="success", and masks secret when !CanReadSecrets
            mockedPropertyValuesPersistentSource.ClearReceivedCalls();
            mockedAuditsPersistentSource.ClearReceivedCalls();
            mockedPropertyEncryptor.EncryptValue(Arg.Any<string>())
                .Returns(_ => $"randomized-nonce-cipher-{Guid.NewGuid():N}");
            mockedPropertyValuesPersistentSource.GetPropertyValuesByName("SecretProp")
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Id = 101,
                        Property = new PropertyApiModel { Id = 1, Name = "SecretProp", Secure = true },
                        PropertyValueFilter = "DevEnv",
                        PropertyValueFilterId = 55,
                        Value = "stored-cipher-v1"
                    }
                });

            mockedApiSecurityService.CanReadSecrets(user, "DevEnv").Returns(false);
            var idempotentNoRead = testService.PutPropertyValues(new List<PropertyValueDto>
            {
                new PropertyValueDto
                {
                    Id = 101,
                    Property = new PropertyApiModel { Name = "SecretProp", Secure = true },
                    PropertyValueFilter = "DevEnv",
                    Value = "decrypted-secret"
                }
            }, user).Single();
            Assert.AreEqual("success", idempotentNoRead.Status);
            Assert.AreEqual(string.Empty, ((PropertyValueDto)idempotentNoRead.Item).Value);
            mockedPropertyValuesPersistentSource.DidNotReceiveWithAnyArgs().UpdatePropertyValue(default, default!);
            mockedAuditsPersistentSource.DidNotReceiveWithAnyArgs().AddRecord(default, default, default!, default!, default!, default!, default!, default!);

            // 6. Idempotent & Changed PUT for Secure IsArray property with a randomized encryptor
            mockedPropertiesPersistentSource.GetProperty("SecretArrayProp")
                .Returns(new PropertyApiModel { Id = 2, Name = "SecretArrayProp", Secure = true, IsArray = true });
            mockedPropertyEncryptor.DecryptValue("enc-elem-1").Returns("alpha");
            mockedPropertyEncryptor.DecryptValue("enc-elem-2").Returns("beta");
            mockedPropertyValuesPersistentSource.GetPropertyValuesByName("SecretArrayProp")
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Id = 202,
                        Property = new PropertyApiModel { Id = 2, Name = "SecretArrayProp", Secure = true, IsArray = true },
                        PropertyValueFilter = "DevEnv",
                        PropertyValueFilterId = 56,
                        Value = "[\"enc-elem-1\",\"enc-elem-2\"]"
                    }
                });

            var idempotentArrayPut = testService.PutPropertyValues(new List<PropertyValueDto>
            {
                new PropertyValueDto
                {
                    Id = 202,
                    Property = new PropertyApiModel { Name = "SecretArrayProp", Secure = true, IsArray = true },
                    PropertyValueFilter = "DevEnv",
                    Value = "[ \"alpha\", \"beta\" ]"
                }
            }, user).Single();
            Assert.AreEqual("success", idempotentArrayPut.Status);
            mockedPropertyValuesPersistentSource.DidNotReceiveWithAnyArgs().UpdatePropertyValue(default, default!);
            mockedAuditsPersistentSource.DidNotReceiveWithAnyArgs().AddRecord(default, default, default!, default!, default!, default!, default!, default!);

            var changedArrayPut = testService.PutPropertyValues(new List<PropertyValueDto>
            {
                new PropertyValueDto
                {
                    Id = 202,
                    Property = new PropertyApiModel { Name = "SecretArrayProp", Secure = true, IsArray = true },
                    PropertyValueFilter = "DevEnv",
                    Value = "[\"alpha\",\"gamma\"]"
                }
            }, user).Single();
            Assert.AreEqual("success", changedArrayPut.Status);
            mockedPropertyValuesPersistentSource.Received(1).UpdatePropertyValue(202, Arg.Any<string>());
            mockedAuditsPersistentSource.Received(1).AddRecord(2, 202, "SecretArrayProp", "DevEnv", "[\"enc-elem-1\",\"enc-elem-2\"]", Arg.Any<string>(), Arg.Any<string>(), "Update");

            // 7. PUT for Non-Secure IsArray property normalizes scalar string into JSON array
            mockedPropertiesPersistentSource.GetProperty("PlainArrayProp")
                .Returns(new PropertyApiModel { Id = 3, Name = "PlainArrayProp", Secure = false, IsArray = true });
            mockedPropertyValuesPersistentSource.GetPropertyValuesByName("PlainArrayProp")
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Id = 303,
                        Property = new PropertyApiModel { Id = 3, Name = "PlainArrayProp", Secure = false, IsArray = true },
                        PropertyValueFilter = "DevEnv",
                        PropertyValueFilterId = 57,
                        Value = "[\"item1\"]"
                    }
                });
            mockedPropertyValuesPersistentSource.UpdatePropertyValue(303, "[\"item2\"]")
                .Returns(new PropertyValueDto
                {
                    Id = 303,
                    Property = new PropertyApiModel { Id = 3, Name = "PlainArrayProp", Secure = false, IsArray = true },
                    PropertyValueFilter = "DevEnv",
                    Value = "[\"item2\"]"
                });

            var normalizedPlainArrayPut = testService.PutPropertyValues(new List<PropertyValueDto>
            {
                new PropertyValueDto
                {
                    Id = 303,
                    Property = new PropertyApiModel { Name = "PlainArrayProp", Secure = false, IsArray = true },
                    PropertyValueFilter = "DevEnv",
                    Value = "item2"
                }
            }, user).Single();
            Assert.AreEqual("success", normalizedPlainArrayPut.Status);
            mockedPropertyValuesPersistentSource.Received(1).UpdatePropertyValue(303, "[\"item2\"]");
        }

        [TestMethod]
        public void ScopedPropertyValueEditor_RemainingBranchesAndExceptionPaths_AreCovered()
        {
            var security = Substitute.For<ISecurityPrivilegesChecker>();
            var encryptor = Substitute.For<IPropertyEncryptor>();
            var propsSource = Substitute.For<IPropertiesPersistentSource>();
            var envsSource = Substitute.For<IEnvironmentsPersistentSource>();
            var propValuesSource = Substitute.For<IPropertyValuesPersistentSource>();
            var auditsSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var roleChecker = Substitute.For<IRolePrivilegesChecker>();
            var log = new MockedLog<ScopedPropertyValueEditor>();
            var claimsReader = Substitute.For<IClaimsPrincipalReader>();
            claimsReader.GetSidsForUser(Arg.Any<ClaimsPrincipal>()).Returns(new List<string> { "sid-1" });

            var editor = new ScopedPropertyValueEditor(
                security, encryptor, propsSource, envsSource, propValuesSource, auditsSource, roleChecker, log, claimsReader);
            var user = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));

            // 1. GetPropertyValues: Secure IsArray=true decryption when CanReadSecrets is true
            envsSource.GetEnvironment("DevEnv", user).Returns(new EnvironmentApiModel { EnvironmentName = "DevEnv" });
            security.CanReadSecrets(user, "DevEnv").Returns(true);
            encryptor.DecryptValue("cipher-a").Returns("plain-a");
            propValuesSource.GetPropertyValuesForUser("DevEnv", "SecArr", Arg.Any<string>(), "sid-1")
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Id = 1,
                        Property = new PropertyApiModel { Name = "SecArr", Secure = true, IsArray = true },
                        PropertyValueFilter = "DevEnv",
                        Value = "[\"cipher-a\"]"
                    }
                });
            var getSecArr = editor.GetPropertyValues("SecArr", "DevEnv", user).Single();
            Assert.AreEqual("[\"plain-a\"]", getSecArr.Value);

            // 2. DeletePropertyValues: Id == 0, RemoveByFilterId true, RemoveByFilterId false + Remove true, and Exception
            security.CanModifyPropertyValue(user, Arg.Any<string>()).Returns(true);
            propsSource.GetProperty(Arg.Any<string>()).Returns(new PropertyApiModel { Id = 1, Name = "DelProp" });

            propValuesSource.GetPropertyValuesByName("ZeroIdProp").Returns(new[]
            {
                new PropertyValueDto { Id = 0, Property = new PropertyApiModel { Name = "ZeroIdProp" }, PropertyValueFilter = null }
            });
            var delZero = editor.DeletePropertyValues(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "ZeroIdProp" }, PropertyValueFilter = null }
            }, user).Single();
            Assert.AreEqual("error: variable value does not exist", delZero.Status);

            propValuesSource.GetPropertyValuesByName("EnvDelProp").Returns(new[]
            {
                new PropertyValueDto { Id = 99, PropertyValueFilter = null, Property = new PropertyApiModel { Name = "EnvDelProp" } },
                new PropertyValueDto { Id = 100, PropertyValueFilter = "OtherEnv", Property = new PropertyApiModel { Name = "EnvDelProp" } },
                new PropertyValueDto { Id = 101, PropertyValueFilterId = 50, PropertyValueFilter = "DevEnv", Property = new PropertyApiModel { Name = "EnvDelProp" }, Value = "v" }
            });
            propValuesSource.RemoveByFilterId(50).Returns(true);
            var delByFilterSuccess = editor.DeletePropertyValues(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "EnvDelProp" }, PropertyValueFilter = "DevEnv" }
            }, user).Single();
            Assert.AreEqual("success", delByFilterSuccess.Status);

            propValuesSource.RemoveByFilterId(50).Returns(false);
            propValuesSource.Remove(101).Returns(true);
            var delByFallbackIdSuccess = editor.DeletePropertyValues(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "EnvDelProp" }, PropertyValueFilter = "DevEnv" }
            }, user).Single();
            Assert.AreEqual("success", delByFallbackIdSuccess.Status);

            propValuesSource.Remove(101).Returns(false);
            var delByFilterAndFallbackFail = editor.DeletePropertyValues(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "EnvDelProp" }, PropertyValueFilter = "DevEnv" }
            }, user).Single();
            Assert.AreEqual("error: deletion from the database failed", delByFilterAndFallbackFail.Status);

            security.CanModifyPropertyValue(user, "DevEnv").Returns(false);
            var postEnvNoPerm = editor.PostPropertyValues(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "ExistingEnvProp" }, PropertyValueFilter = "DevEnv", Value = "v" }
            }, user).Single();
            Assert.IsTrue(postEnvNoPerm.Status.Contains("don't have permissions"));
            security.CanModifyPropertyValue(user, Arg.Any<string>()).Returns(true);

            propValuesSource.GetPropertyValuesByName("DelThrow").Returns(_ => throw new InvalidOperationException("delete failed"));
            var delThrow = editor.DeletePropertyValues(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "DelThrow" } }
            }, user).Single();
            Assert.AreEqual("delete failed", delThrow.Status);

            // 3. PostPropertyValues: existing env value (with null/other filter items in list), existing default value, and Exception
            propValuesSource.GetPropertyValuesByName("ExistingEnvProp").Returns(new[]
            {
                new PropertyValueDto { Id = 10, PropertyValueFilter = null },
                new PropertyValueDto { Id = 11, PropertyValueFilter = "OtherEnv" },
                new PropertyValueDto { Id = 12, PropertyValueFilter = "DevEnv" }
            });
            var postExistingEnv = editor.PostPropertyValues(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "ExistingEnvProp" }, PropertyValueFilter = "DevEnv", Value = "v" }
            }, user).Single();
            Assert.AreEqual("error: variable value for this environment already exists", postExistingEnv.Status);

            propValuesSource.GetPropertyValuesByName("ExistingDefaultProp").Returns(new[]
            {
                new PropertyValueDto { Id = 20, PropertyValueFilterId = 5, PropertyValueFilter = "DevEnv" },
                new PropertyValueDto { Id = 21, PropertyValueFilterId = null, PropertyValueFilter = null }
            });
            var postExistingDef = editor.PostPropertyValues(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "ExistingDefaultProp" }, PropertyValueFilter = null, Value = "v" }
            }, user).Single();
            Assert.AreEqual("error: variable value for this environment already exists", postExistingDef.Status);

            propValuesSource.GetPropertyValuesByName("PostThrow").Returns(_ => throw new InvalidOperationException("post failed"));
            var postThrow = editor.PostPropertyValues(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "PostThrow" }, PropertyValueFilter = null }
            }, user).Single();
            Assert.AreEqual("post failed", postThrow.Status);

            // 4. PutPropertyValues: Secure mismatch, unchanged non-secure value, filter matching branches, and Exception
            propValuesSource.GetPropertyValuesByName("SecMismatchProp").Returns(new[]
            {
                new PropertyValueDto { Id = 30, PropertyValueFilter = null, Property = new PropertyApiModel { Name = "SecMismatchProp", Secure = true } },
                new PropertyValueDto { Id = 31, PropertyValueFilter = "OtherEnv", Property = new PropertyApiModel { Name = "SecMismatchProp", Secure = true } },
                new PropertyValueDto { Id = 32, PropertyValueFilter = "DevEnv", Property = new PropertyApiModel { Name = "SecMismatchProp", Secure = true } }
            });
            var putMismatch = editor.PutPropertyValues(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "SecMismatchProp", Secure = false }, PropertyValueFilter = "DevEnv", Value = "v" }
            }, user).Single();
            Assert.AreEqual("Error: property metadata - secure values don't match, and cannot be edited via this API", putMismatch.Status);

            propValuesSource.GetPropertyValuesByName("UnchangedPlainProp").Returns(new[]
            {
                new PropertyValueDto { Id = 40, PropertyValueFilterId = 9, PropertyValueFilter = "DevEnv", Property = new PropertyApiModel { Name = "UnchangedPlainProp", Secure = false } },
                new PropertyValueDto { Id = 41, PropertyValueFilterId = null, PropertyValueFilter = null, Value = "same-val", Property = new PropertyApiModel { Name = "UnchangedPlainProp", Secure = false } }
            });
            var putUnchangedPlain = editor.PutPropertyValues(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "UnchangedPlainProp", Secure = false }, PropertyValueFilter = null, Value = "same-val" }
            }, user).Single();
            Assert.AreEqual("success", putUnchangedPlain.Status);

            propValuesSource.GetPropertyValuesByName("PutThrow").Returns(_ => throw new InvalidOperationException("put failed"));
            var putThrow = editor.PutPropertyValues(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "PutThrow" }, PropertyValueFilter = null }
            }, user).Single();
            Assert.AreEqual("put failed", putThrow.Status);
        }
    }
}