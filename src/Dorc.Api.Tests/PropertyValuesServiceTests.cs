using System.Security.Claims;
using System.Security.Principal;
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
    public class PropertyValuesServiceTests
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
            var mockedLog = new MockedLog<PropertyValuesService>();
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
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            try
            {
                var result = testService.GetPropertyValues(propertyName, null, new GenericPrincipal(new GenericIdentity("test-user"), null)).FirstOrDefault();
                if (result.Property.Name != propertyName ||
                    result.Property.Secure != secure ||
                    result.Value != propertyValue ||
                    result.PropertyValueFilter != environmentName)
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            mockedClaimsPrincipalReader.GetSidsForUser(Arg.Any<IPrincipal>()).Returns(new List<string>());

            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
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

            try
            {
                var result = testService.GetPropertyValues(propertyName, environmentName, new GenericPrincipal(new GenericIdentity("test-user"), null)).FirstOrDefault();
                if (result.Property.Name != propertyName ||
                    result.Property.Secure != secure ||
                    result.Value != propertyValue ||
                    result.PropertyValueFilter != environmentName)
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            mockedClaimsPrincipalReader.GetSidsForUser(Arg.Any<IPrincipal>()).Returns(new List<string>());

            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
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

            // A result set consisting entirely of secure values used to raise a rights
            // exception while a partially-secure one redacted, so the same caller saw masked
            // data or an error depending on what happened to be in the set. Redaction is now
            // uniform - which matters far more once restricting the privilege to service
            // principals makes this the path every human takes.
            var redacted = testService
                .GetPropertyValues(propertyName, environmentName,
                    new GenericPrincipal(new GenericIdentity("test-user"), null))
                .ToList();

            Assert.AreEqual(1, redacted.Count);
            Assert.AreEqual(string.Empty, redacted[0].Value);
            Assert.AreEqual(propertyName, redacted[0].Property.Name);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            mockedClaimsPrincipalReader.GetSidsForUser(Arg.Any<IPrincipal>()).Returns(new List<string>());

            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
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

            try
            {
                var result = testService.GetPropertyValues(
                    propertyName: String.Empty,
                    environmentName: environmentName,
                    user: new GenericPrincipal(new GenericIdentity("test-user"), null)
                    );
                Assert.IsTrue(result.Where(property => property.Property.Secure).All(property => String.IsNullOrEmpty(property.Value)));
                Assert.IsTrue(result.Where(property => !property.Property.Secure).All(property => !String.IsNullOrEmpty(property.Value)));
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
            }
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
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            mockedClaimsPrincipalReader.GetSidsForUser(Arg.Any<IPrincipal>()).Returns(new List<string>());

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            try
            {
                var result = testService.GetPropertyValues(propertyName, environmentName, new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (result.FirstOrDefault().Property.Name != propertyName ||
                    result.FirstOrDefault().Property.Secure != secure ||
                    result.FirstOrDefault().Value != propertyValue ||
                    result.FirstOrDefault().PropertyValueFilter != environmentName || result.Count() != 1)
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockClaimsPrincipalReader);

            try
            {
                var result = testService.DeletePropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (result.FirstOrDefault().Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            try
            {
                var result = testService.DeletePropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (result.FirstOrDefault().Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
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

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            try
            {
                var result = testService.DeletePropertyValues(new List<PropertyValueDto> { property },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (!result.FirstOrDefault().Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
            }

            mockedPropertyValuesPersistentSource.When(m => m.Remove(Arg.Any<long?>())).Throws(e => new ArgumentException());
            try
            {
                var result = testService.DeletePropertyValues(new List<PropertyValueDto> { property },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (result.FirstOrDefault().Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
            }

            mockedPropertyValuesPersistentSource.Remove(Arg.Any<long?>());
            try
            {
                var result = testService.DeletePropertyValues(new List<PropertyValueDto> { property },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (result.FirstOrDefault().Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
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

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            try
            {
                var result = testService.DeletePropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (!result.FirstOrDefault().Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
            }

            mockedPropertyValuesPersistentSource.Remove(Arg.Any<long?>())
                .Returns(false);
            try
            {
                IEnumerable<Response> result1 = testService.DeletePropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                var first = result1.FirstOrDefault();
                if (first != null && first.Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            try
            {
                var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (result.FirstOrDefault().Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            try
            {
                var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (result.FirstOrDefault().Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
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

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            try
            {
                var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (!result.FirstOrDefault().Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            try
            {
                var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (!result.FirstOrDefault().Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            try
            {
                var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (result.FirstOrDefault().Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            try
            {
                var result = testService.PutPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (!result.FirstOrDefault().Status.StartsWith("error") || !result.FirstOrDefault().Status.Contains("permissions"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            try
            {
                var result = testService.PutPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (result.FirstOrDefault().Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            try
            {
                var result = testService.PutPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (!result.FirstOrDefault().Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker, mockedLog,
                mockedClaimsPrincipalReader);

            try
            {
                var result = testService.PutPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(new GenericIdentity("test-user"), null));
                if (!result.FirstOrDefault().Status.Equals("success"))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
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
            var mockedLog = new MockedLog<PropertyValuesService>();
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

            var testService = new PropertyValuesService(
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
            var mockedLog = new MockedLog<PropertyValuesService>();
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

            var testService = new PropertyValuesService(
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
        }

        [TestMethod]
        public void PostAndPutPropertyValues_ErrorAndConflictResponses_RedactSecureValuesUnlessCanReadSecrets()
        {
            var mockedApiSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockedPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockedEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockedAuditsPersistentSource = Substitute.For<IPropertyValuesAuditPersistentSource>();
            var mockedPrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockedLog = new MockedLog<PropertyValuesService>();
            var mockedClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var user = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));
            const string envName = "DevEnv";
            const string secretProp = "SecretProp";
            const string plainSecret = "PlainSecretLeakCandidate!";

            mockedPropertiesPersistentSource.GetProperty(secretProp)
                .Returns(new PropertyApiModel { Id = 1, Name = secretProp, Secure = true });
            mockedApiSecurityService.CanReadSecrets(user, Arg.Any<string>()).Returns(false);

            var testService = new PropertyValuesService(
                mockedApiSecurityService,
                mockedPropertyEncryptor,
                mockedPropertiesPersistentSource,
                mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource,
                mockedAuditsPersistentSource,
                mockedPrivilegesChecker,
                mockedLog,
                mockedClaimsPrincipalReader);

            PropertyValueDto MakeDto(bool secure = true, string env = envName) => new()
            {
                Property = new PropertyApiModel { Name = secretProp, Secure = secure },
                PropertyValueFilter = env,
                Value = plainSecret
            };

            // 1. POST: environment does not exist -> redacted
            mockedEnvironmentsPersistentSource.GetEnvironment("MissingEnv", user).Returns((EnvironmentApiModel)null!);
            var postMissingEnv = testService.PostPropertyValues(new[] { MakeDto(env: "MissingEnv") }, user).Single();
            StringAssert.Contains(postMissingEnv.Status, "does not exist");
            Assert.AreEqual(string.Empty, ((PropertyValueDto)postMissingEnv.Item).Value);

            // 2. POST: lacking CanModifyPropertyValue -> redacted
            mockedEnvironmentsPersistentSource.GetEnvironment(envName, user).Returns(new EnvironmentApiModel { EnvironmentName = envName });
            mockedApiSecurityService.CanModifyPropertyValue(user, envName).Returns(false);
            var postNoPerm = testService.PostPropertyValues(new[] { MakeDto() }, user).Single();
            StringAssert.Contains(postNoPerm.Status, "permissions");
            Assert.AreEqual(string.Empty, ((PropertyValueDto)postNoPerm.Item).Value);

            // 3. POST: already exists -> redacted when CanReadSecrets is false, preserved when CanReadSecrets is true
            mockedApiSecurityService.CanModifyPropertyValue(user, envName).Returns(true);
            mockedPropertyValuesPersistentSource.GetPropertyValuesByName(secretProp).Returns(new[]
            {
                new PropertyValueDto
                {
                    Id = 77,
                    Property = new PropertyApiModel { Id = 1, Name = secretProp, Secure = true },
                    PropertyValueFilter = envName,
                    PropertyValueFilterId = 5,
                    Value = "ENC(existing)"
                }
            });

            var postExistsNoRead = testService.PostPropertyValues(new[] { MakeDto() }, user).Single();
            StringAssert.Contains(postExistsNoRead.Status, "already exists");
            Assert.AreEqual(string.Empty, ((PropertyValueDto)postExistsNoRead.Item).Value);

            mockedApiSecurityService.CanReadSecrets(user, envName).Returns(true);
            var postExistsWithRead = testService.PostPropertyValues(new[] { MakeDto() }, user).Single();
            StringAssert.Contains(postExistsWithRead.Status, "already exists");
            Assert.AreEqual(plainSecret, ((PropertyValueDto)postExistsWithRead.Item).Value);
            mockedApiSecurityService.CanReadSecrets(user, envName).Returns(false);

            // 4. PUT: lacking CanModifyPropertyValue -> redacted
            mockedApiSecurityService.CanModifyPropertyValue(user, envName).Returns(false);
            var putNoPerm = testService.PutPropertyValues(new[] { MakeDto() }, user).Single();
            StringAssert.Contains(putNoPerm.Status, "permissions");
            Assert.AreEqual(string.Empty, ((PropertyValueDto)putNoPerm.Item).Value);

            // 5. PUT: metadata mismatch (caller sends Secure=false against DB Secure=true) -> still redacted
            mockedApiSecurityService.CanModifyPropertyValue(user, envName).Returns(true);
            var putMismatch = testService.PutPropertyValues(new[] { MakeDto(secure: false) }, user).Single();
            StringAssert.Contains(putMismatch.Status, "secure values don't match");
            Assert.AreEqual(string.Empty, ((PropertyValueDto)putMismatch.Item).Value);

            // 6. PUT: variable value does not exist -> redacted
            mockedPropertyValuesPersistentSource.GetPropertyValuesByName(secretProp).Returns(Array.Empty<PropertyValueDto>());
            var putNotFound = testService.PutPropertyValues(new[] { MakeDto() }, user).Single();
            StringAssert.Contains(putNotFound.Status, "does not exist");
            Assert.AreEqual(string.Empty, ((PropertyValueDto)putNotFound.Item).Value);

            // 7. POST: unhandled exception from persistent source -> redacted
            mockedPropertyValuesPersistentSource
                .When(s => s.AddPropertyValue(Arg.Any<PropertyValueDto>()))
                .Do(_ => throw new InvalidOperationException("simulated db failure"));
            var postException = testService.PostPropertyValues(new[] { MakeDto() }, user).Single();
            Assert.AreEqual("simulated db failure", postException.Status);
            Assert.AreEqual(string.Empty, ((PropertyValueDto)postException.Item).Value);
        }
    }
}