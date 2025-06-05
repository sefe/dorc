using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;
using System.Security.Claims;
using Dorc.Api.Services;
using Dorc.PersistentData;
using Dorc.Api.Tests.Mocks;
namespace Dorc.Api.Tests
{
    [TestClass]
    public class PropertiesServiceTests
    {
        private readonly ClaimsPrincipal _user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[] {
            new Claim(ClaimTypes.NameIdentifier, "SomeValueHere"),
            new Claim(ClaimTypes.Name, "gunnar@somecompany.com")
            // other required and custom claims
        }, "TestAuthentication"));

        [TestMethod]
        public void PostPropertiesTestSuccessfulCase()
        {
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockPvs = Substitute.For<IPropertyValuesService>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            mockPropertiesPersistentSource
                .CreateProperty(Arg.Any<PropertyApiModel>(), Arg.Any<string>())
                .Returns(x => x.ArgAt<PropertyApiModel>(0));
            var happyService = new PropertiesService(mockPropertiesPersistentSource, mockPvs, mockClaimsPrincipalReader);
            var correctInput = new List<PropertyApiModel>
                {new PropertyApiModel {Name = "valid property name", Secure = true}};
            try
            {
                var happyResult = happyService.PostProperties(correctInput, _user).ToList().FirstOrDefault();
                if (happyResult == null || !happyResult.Status.Equals("success") ||
                    !((PropertyApiModel)happyResult.Item).Name.Equals(correctInput[0].Name) ||
                    !((PropertyApiModel)happyResult.Item).Secure.Equals(correctInput[0].Secure))
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
        public void PostPropertiesTestErrorCase()
        {
            const string testErrorMessage = "test: Failed property creation";
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockPvs = Substitute.For<IPropertyValuesService>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            mockPropertiesPersistentSource
                .When(w => w.CreateProperty(Arg.Any<PropertyApiModel>(), Arg.Any<string>()))
                .Throw(c => new Exception(testErrorMessage));
            var erroredService = new PropertiesService(mockPropertiesPersistentSource, mockPvs, mockClaimsPrincipalReader);
            var wrongInput = new List<PropertyApiModel>
                {new PropertyApiModel {Name = "Invalid property name", Secure = false}};
            try
            {
                var erroredResult = erroredService.PostProperties(wrongInput, _user).ToList();
                if (erroredResult.Count != 1 || !erroredResult.FirstOrDefault().Status.Equals(testErrorMessage))
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
        public void DeletePropertiesTestSuccessfulCase()
        {
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockPvs = Substitute.For<IPropertyValuesService>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            mockPropertiesPersistentSource.DeleteProperty(Arg.Any<string>(), Arg.Any<string>())
                .Returns(true);
            var happyService = new PropertiesService(mockPropertiesPersistentSource, mockPvs, mockClaimsPrincipalReader);

            var correctInput = new List<string>
                {"Valid property Name"};
            try
            {
                var happyResult = happyService.DeleteProperties(correctInput, _user).ToList();
                if (happyResult.Count != 1 || !happyResult.FirstOrDefault().Status.Equals("success") ||
                    !((string)happyResult.FirstOrDefault().Item).Equals(correctInput.FirstOrDefault()))
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
        public void DeletePropertiesTestErrorCase()
        {
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            //const string testErrorMessage = "test: Failed property deletion";
            mockPropertiesPersistentSource.DeleteProperty(Arg.Any<string>(), string.Empty).Returns(false);
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var erroredService = new PropertiesService(mockPropertiesPersistentSource, Substitute.For<IPropertyValuesService>(),
                mockClaimsPrincipalReader);

            var incorrectInput = new List<string>
                {"Invalid property Name"};
            try
            {
                var badResult = erroredService.DeleteProperties(incorrectInput, _user).ToList();
                if (badResult.Count != 1 || badResult.FirstOrDefault().Status.Equals("success") ||
                    !((string)badResult.FirstOrDefault().Item).Equals(incorrectInput.FirstOrDefault()))
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
        public void UpdatePropertiesTestSuccessfulCase()
        {
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var testProperty = new PropertyApiModel { Name = "PropertyName", Secure = false };

            mockPropertiesPersistentSource.UpdateProperty(Arg.Any<PropertyApiModel>())
                .Returns(true);

            var happyService = new PropertiesService(mockPropertiesPersistentSource, Substitute.For<IPropertyValuesService>(),
                mockClaimsPrincipalReader);
            var correctInput = new Dictionary<string, PropertyApiModel>
                {{testProperty.Name, testProperty}};
            try
            {
                var happyResult = happyService.PutProperties(correctInput, _user).ToList();
                if (happyResult.Count != 1 || !happyResult.FirstOrDefault().Status.Equals("success") ||
                    !((KeyValuePair<string, PropertyApiModel>)happyResult.FirstOrDefault().Item).Value.Name.Equals(
                        testProperty.Name) ||
                    !((KeyValuePair<string, PropertyApiModel>)happyResult.FirstOrDefault().Item).Value.Secure.Equals(
                        testProperty.Secure))
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
        public void UpdatePropertiesTestFailedGetPropertyCase()
        {
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            var testProperty = new PropertyApiModel { Name = "PropertyName", Secure = false };

            mockPropertiesPersistentSource.UpdateProperty(Arg.Any<PropertyApiModel>())
                .Returns(false);

            var service = new PropertiesService(mockPropertiesPersistentSource, Substitute.For<IPropertyValuesService>(),
                mockClaimsPrincipalReader);

            var correctInput = new Dictionary<string, PropertyApiModel>
                {{testProperty.Name, testProperty}};
            try
            {
                var badResult = service.PutProperties(correctInput, _user).ToList();
                if (badResult.Count != 1 || badResult.FirstOrDefault().Status.Equals("success"))
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
        public void UpdatePropertiesTestFailedPropertyUpdateCase()
        {
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var testProperty = new PropertyApiModel { Name = "PropertyName", Secure = false };

            mockPropertiesPersistentSource.UpdateProperty(Arg.Any<PropertyApiModel>())
                .Returns(false);

            var service = new PropertiesService(mockPropertiesPersistentSource, Substitute.For<IPropertyValuesService>(),
                mockClaimsPrincipalReader);
            var correctInput = new Dictionary<string, PropertyApiModel>
                {{testProperty.Name, testProperty}};
            try
            {
                var badResult = service.PutProperties(correctInput, _user).ToList();
                if (badResult.Count != 1 || badResult.FirstOrDefault().Status.Equals("success"))
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
