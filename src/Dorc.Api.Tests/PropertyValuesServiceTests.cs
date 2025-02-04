using System.Security.Claims;
using System.Security.Principal;
using Dorc.Api.Services;
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

            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());

            mockedPropertyValuesPersistentSource.GetPropertyValuesByName(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
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
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.GetPropertyValues(propertyName, null, new GenericPrincipal(WindowsIdentity.GetCurrent(), null)).FirstOrDefault();
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

            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());

            mockedPropertyValuesPersistentSource.GetPropertyValuesByName(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = propertyName, Secure = secure}, PropertyValueFilter = environmentName, Value = propertyValue
                    }
                });

            try
            {
                var result = testService.GetPropertyValues(propertyName, environmentName, new GenericPrincipal(WindowsIdentity.GetCurrent(), null)).FirstOrDefault();
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

            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());

            mockedPropertyValuesPersistentSource.GetPropertyValuesByName(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel{Name = propertyName, Secure = secure}, PropertyValueFilter = environmentName, Value = propertyValue
                    }
                });

            Assert.ThrowsException<NonEnoughRightsException>(() => testService.GetPropertyValues(propertyName, environmentName, new GenericPrincipal(WindowsIdentity.GetCurrent(), null)));
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

            var mockedPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            mockedEnvironmentsPersistentSource.GetEnvironment(Arg
                        .Any<string>())
                .Returns(new EnvironmentApiModel());

            mockedPropertyValuesPersistentSource.GetEnvironmentProperties(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
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
                    user: new GenericPrincipal(WindowsIdentity.GetCurrent(), null)
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
            mockedPropertyValuesPersistentSource.GetPropertyValuesByName(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
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
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.GetPropertyValues(propertyName, environmentName, new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.DeletePropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.DeletePropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.DeletePropertyValues(new List<PropertyValueDto> { property },
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.DeletePropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.PostPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.PutPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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
            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.PutPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.PutPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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

            var testService = new PropertyValuesService(mockedApiSecurityService, mockedPropertyEncryptor,
                mockedPropertiesPersistentSource, mockedEnvironmentsPersistentSource,
                mockedPropertyValuesPersistentSource, mockedAuditsPersistentSource, mockedPrivilegesChecker);

            try
            {
                var result = testService.PutPropertyValues(new List<PropertyValueDto> { testProperty },
                    new GenericPrincipal(WindowsIdentity.GetCurrent(), null));
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
    }
}