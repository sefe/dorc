using Dorc.Api.Properties;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;
using System.Security.Claims;
using Dorc.Api.Services;
using Dorc.PersistentData;
using Dorc.Api.Tests.Mocks;
using Microsoft.Extensions.Logging;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class PropertyDefinitionEditorTests
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
            var mockPvs = Substitute.For<IScopedPropertyValueEditor>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var mockPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockLogger = Substitute.For<ILogger<PropertyDefinitionEditor>>();

            mockPropertiesPersistentSource
                .CreateProperty(Arg.Any<PropertyApiModel>(), Arg.Any<string>())
                .Returns(x => x.ArgAt<PropertyApiModel>(0));
            var happyService = new PropertyDefinitionEditor(mockPropertiesPersistentSource, mockPvs, mockClaimsPrincipalReader, mockPropertyValuesPersistentSource, mockPropertyEncryptor, mockLogger);
            var correctInput = new List<PropertyApiModel>
                {new PropertyApiModel {Name = "valid property name", Secure = true}};
            var happyResult = happyService.PostProperties(correctInput, _user).ToList().FirstOrDefault();
            if (happyResult == null || !happyResult.Status.Equals("success") ||
                !((PropertyApiModel)happyResult.Item).Name.Equals(correctInput[0].Name) ||
                !((PropertyApiModel)happyResult.Item).Secure.Equals(correctInput[0].Secure))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void PostPropertiesTestErrorCase()
        {
            const string testErrorMessage = "test: Failed property creation";
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockPvs = Substitute.For<IScopedPropertyValueEditor>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var mockPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockLogger = Substitute.For<ILogger<PropertyDefinitionEditor>>();

            mockPropertiesPersistentSource
                .When(w => w.CreateProperty(Arg.Any<PropertyApiModel>(), Arg.Any<string>()))
                .Throw(c => new Exception(testErrorMessage));
            var erroredService = new PropertyDefinitionEditor(mockPropertiesPersistentSource, mockPvs, mockClaimsPrincipalReader, mockPropertyValuesPersistentSource, mockPropertyEncryptor, mockLogger);
            var wrongInput = new List<PropertyApiModel>
                {new PropertyApiModel {Name = "Invalid property name", Secure = false}};
            var erroredResult = erroredService.PostProperties(wrongInput, _user).ToList();
            if (erroredResult.Count != 1 || !erroredResult.FirstOrDefault().Status.Equals(testErrorMessage))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void DeletePropertiesTestSuccessfulCase()
        {
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockPvs = Substitute.For<IScopedPropertyValueEditor>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var mockPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockLogger = Substitute.For<ILogger<PropertyDefinitionEditor>>();

            mockPropertiesPersistentSource.DeleteProperty(Arg.Any<string>(), Arg.Any<string>())
                .Returns(true);
            var happyService = new PropertyDefinitionEditor(mockPropertiesPersistentSource, mockPvs, mockClaimsPrincipalReader, mockPropertyValuesPersistentSource, mockPropertyEncryptor, mockLogger);

            var correctInput = new List<string>
                {"Valid property Name"};
            var happyResult = happyService.DeleteProperties(correctInput, _user).ToList();
            if (happyResult.Count != 1 || !happyResult.FirstOrDefault().Status.Equals("success") ||
                !((string)happyResult.FirstOrDefault().Item).Equals(correctInput.FirstOrDefault()))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void DeletePropertiesTestErrorCase()
        {
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            //const string testErrorMessage = "test: Failed property deletion";
            mockPropertiesPersistentSource.DeleteProperty(Arg.Any<string>(), string.Empty).Returns(false);
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var mockLogger = Substitute.For<ILogger<PropertyDefinitionEditor>>();
            var erroredService = new PropertyDefinitionEditor(mockPropertiesPersistentSource, Substitute.For<IScopedPropertyValueEditor>(),
                mockClaimsPrincipalReader, Substitute.For<IPropertyValuesPersistentSource>(), Substitute.For<IPropertyEncryptor>(), mockLogger);

            var incorrectInput = new List<string>
                {"Invalid property Name"};
            var badResult = erroredService.DeleteProperties(incorrectInput, _user).ToList();
            if (badResult.Count != 1 || badResult.FirstOrDefault().Status.Equals("success") ||
                !((string)badResult.FirstOrDefault().Item).Equals(incorrectInput.FirstOrDefault()))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void UpdatePropertiesTestSuccessfulCase()
        {
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var testProperty = new PropertyApiModel { Name = "PropertyName", Secure = false };
            var mockLogger = Substitute.For<ILogger<PropertyDefinitionEditor>>();

            // Setup the mock to return the current property for GetProperty call
            mockPropertiesPersistentSource.GetProperty(testProperty.Name).Returns(testProperty);
            mockPropertiesPersistentSource.UpdateProperty(Arg.Any<PropertyApiModel>())
                .Returns(true);

            var happyService = new PropertyDefinitionEditor(mockPropertiesPersistentSource, Substitute.For<IScopedPropertyValueEditor>(),
                mockClaimsPrincipalReader, Substitute.For<IPropertyValuesPersistentSource>(), Substitute.For<IPropertyEncryptor>(), mockLogger);
            var correctInput = new Dictionary<string, PropertyApiModel>
                {{testProperty.Name, testProperty}};
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

        [TestMethod]
        public void UpdatePropertiesTestFailedGetPropertyCase()
        {
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var mockLogger = Substitute.For<ILogger<PropertyDefinitionEditor>>();

            var testProperty = new PropertyApiModel { Name = "PropertyName", Secure = false };

            // Setup GetProperty to return null to simulate property not found
            mockPropertiesPersistentSource.GetProperty(testProperty.Name).Returns((PropertyApiModel)null);
            mockPropertiesPersistentSource.UpdateProperty(Arg.Any<PropertyApiModel>())
                .Returns(false);

            var service = new PropertyDefinitionEditor(mockPropertiesPersistentSource, Substitute.For<IScopedPropertyValueEditor>(),
                mockClaimsPrincipalReader, Substitute.For<IPropertyValuesPersistentSource>(), Substitute.For<IPropertyEncryptor>(), mockLogger);

            var correctInput = new Dictionary<string, PropertyApiModel>
                {{testProperty.Name, testProperty}};
            var badResult = service.PutProperties(correctInput, _user).ToList();
            if (badResult.Count != 1 || badResult.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void UpdatePropertiesTestFailedPropertyUpdateCase()
        {
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var testProperty = new PropertyApiModel { Name = "PropertyName", Secure = false };
            var mockLogger = Substitute.For<ILogger<PropertyDefinitionEditor>>();

            // Setup GetProperty to return the property so we get past the null check
            mockPropertiesPersistentSource.GetProperty(testProperty.Name).Returns(testProperty);
            mockPropertiesPersistentSource.UpdateProperty(Arg.Any<PropertyApiModel>())
                .Returns(false);

            var service = new PropertyDefinitionEditor(mockPropertiesPersistentSource, Substitute.For<IScopedPropertyValueEditor>(),
                mockClaimsPrincipalReader, Substitute.For<IPropertyValuesPersistentSource>(), Substitute.For<IPropertyEncryptor>(), mockLogger);
            var correctInput = new Dictionary<string, PropertyApiModel>
                {{testProperty.Name, testProperty}};
            var badResult = service.PutProperties(correctInput, _user).ToList();
            if (badResult.Count != 1 || badResult.FirstOrDefault().Status.Equals("success"))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public void PutPropertiesEncryptsValuesWhenChangedToSecure()
        {
            // Arrange
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockPvs = Substitute.For<IScopedPropertyValueEditor>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var mockPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockLogger = Substitute.For<ILogger<PropertyDefinitionEditor>>();

            const string propertyName = "TestProperty";
            const string originalValue = "plaintext_value";
            const string encryptedValue = "encrypted_value";

            var currentProperty = new PropertyApiModel
            {
                Id = 1,
                Name = propertyName,
                Secure = false // Currently non-secure
            };

            var updatedProperty = new PropertyApiModel
            {
                Id = 1,
                Name = propertyName,
                Secure = true // Changing to secure
            };

            var existingPropertyValues = new[]
            {
                new PropertyValueDto
                {
                    Id = 1,
                    Value = originalValue,
                    Property = currentProperty,
                    PropertyValueFilter = "TestEnv"
                }
            };

            // Setup mock behaviors
            mockPropertiesPersistentSource.GetProperty(propertyName).Returns(currentProperty);
            mockPropertiesPersistentSource.UpdateProperty(updatedProperty).Returns(true);
            mockPropertyValuesPersistentSource.GetPropertyValuesByName(propertyName).Returns(existingPropertyValues);
            mockPropertyEncryptor.EncryptValue(originalValue).Returns(encryptedValue);
            mockPropertyValuesPersistentSource.UpdatePropertyValue(1, encryptedValue).Returns(existingPropertyValues[0]);

            var service = new PropertyDefinitionEditor(
                mockPropertiesPersistentSource, 
                mockPvs, 
                mockClaimsPrincipalReader, 
                mockPropertyValuesPersistentSource, 
                mockPropertyEncryptor, mockLogger);

            var propertiesToUpdate = new Dictionary<string, PropertyApiModel>
            {
                { propertyName, updatedProperty }
            };

            // Act
            var results = service.PutProperties(propertiesToUpdate, _user);

            // Assert
            var resultsList = results.ToList();
            Assert.AreEqual(1, resultsList.Count);
            Assert.AreEqual("success", resultsList[0].Status);

            // Verify encryption workflow was triggered
            mockPropertiesPersistentSource.Received(1).GetProperty(propertyName);
            mockPropertiesPersistentSource.Received(1).UpdateProperty(updatedProperty);
            mockPropertyValuesPersistentSource.Received(1).GetPropertyValuesByName(propertyName);
            mockPropertyEncryptor.Received(1).EncryptValue(originalValue);
            mockPropertyValuesPersistentSource.Received(1).UpdatePropertyValue(1, encryptedValue);
        }

        [TestMethod]
        public void PutPropertiesDoesNotEncryptWhenChangedToNonSecure()
        {
            // Arrange
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockPvs = Substitute.For<IScopedPropertyValueEditor>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var mockPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockLogger = Substitute.For<ILogger<PropertyDefinitionEditor>>();

            const string propertyName = "TestProperty";

            var currentProperty = new PropertyApiModel
            {
                Id = 1,
                Name = propertyName,
                Secure = true // Currently secure
            };

            var updatedProperty = new PropertyApiModel
            {
                Id = 1,
                Name = propertyName,
                Secure = false // Changing to non-secure
            };

            // Setup mock behaviors
            mockPropertiesPersistentSource.GetProperty(propertyName).Returns(currentProperty);
            mockPropertiesPersistentSource.UpdateProperty(updatedProperty).Returns(true);

            var service = new PropertyDefinitionEditor(
                mockPropertiesPersistentSource, 
                mockPvs, 
                mockClaimsPrincipalReader, 
                mockPropertyValuesPersistentSource, 
                mockPropertyEncryptor, mockLogger);

            var propertiesToUpdate = new Dictionary<string, PropertyApiModel>
            {
                { propertyName, updatedProperty }
            };

            // Act
            var results = service.PutProperties(propertiesToUpdate, _user);

            // Assert
            var resultsList = results.ToList();
            Assert.AreEqual(1, resultsList.Count);
            Assert.AreEqual("success", resultsList[0].Status);

            // Verify encryption workflow was NOT triggered
            mockPropertiesPersistentSource.Received(1).GetProperty(propertyName);
            mockPropertiesPersistentSource.Received(1).UpdateProperty(updatedProperty);
            mockPropertyValuesPersistentSource.DidNotReceive().GetPropertyValuesByName(Arg.Any<string>());
            mockPropertyEncryptor.DidNotReceive().EncryptValue(Arg.Any<string>());
            mockPropertyValuesPersistentSource.DidNotReceive().UpdatePropertyValue(Arg.Any<long>(), Arg.Any<string>());
        }

        [TestMethod]
        public void ImportPropertiesRunsPostAndPutPropertyValuesWorkflow()
        {
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockPvs = Substitute.For<IScopedPropertyValueEditor>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var mockPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockLogger = Substitute.For<ILogger<PropertyDefinitionEditor>>();

            mockPropertiesPersistentSource
                .CreateProperty(Arg.Any<PropertyApiModel>(), Arg.Any<string>())
                .Returns(x => x.ArgAt<PropertyApiModel>(0));

            mockPvs.PostPropertyValues(Arg.Any<IEnumerable<PropertyValueDto>>(), Arg.Any<ClaimsPrincipal>())
                .Returns(new List<Response> { new Response { Item = "values", Status = "success" } });
            mockPvs.PutPropertyValues(Arg.Any<IEnumerable<PropertyValueDto>>(), Arg.Any<ClaimsPrincipal>())
                .Returns(new List<Response> { new Response { Item = "values", Status = "success" } });

            var service = new PropertyDefinitionEditor(
                mockPropertiesPersistentSource,
                mockPvs,
                mockClaimsPrincipalReader,
                mockPropertyValuesPersistentSource,
                mockPropertyEncryptor,
                mockLogger);

            var rows = new[]
            {
                new PropertyImportCsvDto
                {
                    PropertyName = "ABAgentID",
                    Value = "Test_DREPIN",
                    IsSecure = false,
                    Environment = "EnvA"
                }
            };

            var result = service.ImportProperties(rows, _user).ToList();

            Assert.IsTrue(result.Any(r => r.Status == "success"));
            mockPvs.Received(1).PostPropertyValues(Arg.Any<IEnumerable<PropertyValueDto>>(), _user);
            mockPvs.Received(1).PutPropertyValues(Arg.Any<IEnumerable<PropertyValueDto>>(), _user);
        }

        [TestMethod]
        public void PutPropertiesResolvesByDictionaryKeyWhenRenamingPropertyAndPreservesId()
        {
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockPvs = Substitute.For<IScopedPropertyValueEditor>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var mockPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockLogger = Substitute.For<ILogger<PropertyDefinitionEditor>>();

            var existingProperty = new PropertyApiModel
            {
                Id = 42,
                Name = "OriginalName",
                Secure = false,
                IsArray = true
            };
            var renamedPayload = new PropertyApiModel
            {
                Id = 0,
                Name = "RenamedName",
                Secure = false,
                IsArray = true
            };

            mockPropertiesPersistentSource.GetProperty("OriginalName").Returns(existingProperty);
            mockPropertiesPersistentSource.UpdateProperty(Arg.Is<PropertyApiModel>(p => p.Id == 42 && p.Name == "RenamedName" && p.IsArray)).Returns(true);

            var service = new PropertyDefinitionEditor(
                mockPropertiesPersistentSource,
                mockPvs,
                mockClaimsPrincipalReader,
                mockPropertyValuesPersistentSource,
                mockPropertyEncryptor,
                mockLogger);

            var result = service.PutProperties(new Dictionary<string, PropertyApiModel>
            {
                ["OriginalName"] = renamedPayload
            }, _user).ToList();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("success", result[0].Status);
            Assert.AreEqual(42, renamedPayload.Id);
            mockPropertiesPersistentSource.Received(1).UpdateProperty(Arg.Is<PropertyApiModel>(p => p.Id == 42 && p.Name == "RenamedName" && p.IsArray));
        }

        [TestMethod]
        public void ImportPropertiesReturnsErrorWhenPropertyNameOrValueBlank()
        {
            var service = new PropertyDefinitionEditor(
                Substitute.For<IPropertiesPersistentSource>(),
                Substitute.For<IScopedPropertyValueEditor>(),
                Substitute.For<IClaimsPrincipalReader>(),
                Substitute.For<IPropertyValuesPersistentSource>(),
                Substitute.For<IPropertyEncryptor>(),
                Substitute.For<ILogger<PropertyDefinitionEditor>>());

            var rows = new[]
            {
                new PropertyImportCsvDto
                {
                    PropertyName = "",
                    Value = "",
                    IsSecure = false
                }
            };

            var result = service.ImportProperties(rows, _user).ToList();

            Assert.IsTrue(result.Any(r => r.Status.Contains("PropertyName")));
            Assert.IsTrue(result.Any(r => r.Status.Contains("Property value")));
        }

        [TestMethod]
        public void PutPropertiesEncryptsArrayElementsIndividuallyWhenChangedToSecure()
        {
            var mockPropertiesPersistentSource = Substitute.For<IPropertiesPersistentSource>();
            var mockPvs = Substitute.For<IScopedPropertyValueEditor>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var mockPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            var mockPropertyEncryptor = Substitute.For<IPropertyEncryptor>();
            var mockLogger = Substitute.For<ILogger<PropertyDefinitionEditor>>();

            mockPropertyEncryptor.EncryptValue(Arg.Any<string>()).Returns(ci => "enc:" + ci.Arg<string>());

            var currentArrayProp = new PropertyApiModel { Id = 10, Name = "ExistingArrayProp", Secure = false, IsArray = true };
            var updatedArrayProp = new PropertyApiModel { Id = 10, Name = "ExistingArrayProp", Secure = true, IsArray = true };

            var currentScalarPromotedToArray = new PropertyApiModel { Id = 11, Name = "PromotedArrayProp", Secure = false, IsArray = false };
            var updatedScalarPromotedToArray = new PropertyApiModel { Id = 11, Name = "PromotedArrayProp", Secure = true, IsArray = true };

            mockPropertiesPersistentSource.GetProperty("ExistingArrayProp").Returns(currentArrayProp);
            mockPropertiesPersistentSource.GetProperty("PromotedArrayProp").Returns(currentScalarPromotedToArray);
            mockPropertiesPersistentSource.UpdateProperty(Arg.Any<PropertyApiModel>()).Returns(true);

            mockPropertyValuesPersistentSource.GetPropertyValuesByName("ExistingArrayProp").Returns(new[]
            {
                new PropertyValueDto { Id = 501, Value = "[\"alpha\",\"beta\"]", Property = currentArrayProp }
            });
            mockPropertyValuesPersistentSource.GetPropertyValuesByName("PromotedArrayProp").Returns(new[]
            {
                new PropertyValueDto { Id = 502, Value = "[\"gamma\"]", Property = currentScalarPromotedToArray }
            });

            var editor = new PropertyDefinitionEditor(
                mockPropertiesPersistentSource,
                mockPvs,
                mockClaimsPrincipalReader,
                mockPropertyValuesPersistentSource,
                mockPropertyEncryptor,
                mockLogger);

            var results = editor.PutProperties(new Dictionary<string, PropertyApiModel>
            {
                ["ExistingArrayProp"] = updatedArrayProp,
                ["PromotedArrayProp"] = updatedScalarPromotedToArray
            }, _user).ToList();

            Assert.HasCount(2, results);
            Assert.IsTrue(results.All(r => r.Status == "success"));
            mockPropertyValuesPersistentSource.Received(1).UpdatePropertyValue(501, "[\"enc:alpha\",\"enc:beta\"]");
            mockPropertyValuesPersistentSource.Received(1).UpdatePropertyValue(502, "[\"enc:gamma\"]");
        }

        [TestMethod]
        public void PropertyDefinitionEditor_AllBranchesAndExceptionPaths_AreCovered()
        {
            var propsSource = Substitute.For<IPropertiesPersistentSource>();
            var pvs = Substitute.For<IScopedPropertyValueEditor>();
            var claimsReader = Substitute.For<IClaimsPrincipalReader>();
            var propValuesSource = Substitute.For<IPropertyValuesPersistentSource>();
            var encryptor = Substitute.For<IPropertyEncryptor>();
            var logger = Substitute.For<ILogger<PropertyDefinitionEditor>>();

            var editor = new PropertyDefinitionEditor(propsSource, pvs, claimsReader, propValuesSource, encryptor, logger);

            // 1. GetProperty & GetProperties success and exception paths
            var expectedProp = new PropertyApiModel { Id = 1, Name = "P1" };
            propsSource.GetProperty("P1").Returns(expectedProp);
            Assert.AreSame(expectedProp, editor.GetProperty("P1"));

            propsSource.GetProperty("Boom").Returns(_ => throw new InvalidOperationException("get failed"));
            Assert.IsNull(editor.GetProperty("Boom"));

            propsSource.GetAllProperties().Returns(new[] { expectedProp });
            Assert.AreEqual(1, editor.GetProperties().Count());

            propsSource.GetAllProperties().Returns(_ => throw new InvalidOperationException("getall failed"));
            Assert.AreEqual(0, editor.GetProperties().Count());

            // 2. DeleteProperties exception with inner exception unrolling
            pvs.GetPropertyValues("ThrowInner", null, _user)
                .Returns(_ => throw new Exception("See inner exception for details", new Exception("Root cause")));
            var delRes = editor.DeleteProperties(new[] { "ThrowInner" }, _user).Single();
            Assert.AreEqual("Root cause", delRes.Status);

            // 3. PostProperties blank name and exception path
            var postBlank = editor.PostProperties(new[] { new PropertyApiModel { Name = "   " } }, _user).Single();
            Assert.AreEqual("error : Please specify a Variable Name", postBlank.Status);

            propsSource.When(x => x.CreateProperty(Arg.Is<PropertyApiModel>(p => p.Name == "ThrowPost"), Arg.Any<string>()))
                .Do(_ => throw new Exception("Create failed"));
            var postThrow = editor.PostProperties(new[] { new PropertyApiModel { Name = "ThrowPost" } }, _user).Single();
            Assert.AreEqual("Create failed", postThrow.Status);

            // 4. PutProperties key/name fallbacks, NullReferenceException, general Exception, and EncryptExistingPropertyValues branches
            var propByValName = new PropertyApiModel { Id = 20, Name = "ByValName", Secure = false };
            propsSource.GetProperty("ByValName").Returns(propByValName);
            propsSource.GetProperty("KeyFillName").Returns(new PropertyApiModel { Id = 21, Name = "KeyFillName", Secure = false });
            propsSource.GetProperty("OldKeyMiss").Returns((PropertyApiModel)null!);
            propsSource.GetProperty("FallbackTarget").Returns(new PropertyApiModel { Id = 22, Name = "FallbackTarget", Secure = false });
            propsSource.UpdateProperty(Arg.Any<PropertyApiModel>()).Returns(true);

            // EncryptExistingPropertyValues: null list, empty list, empty value string, and exception
            propValuesSource.GetPropertyValuesByName("ByValName").Returns((IEnumerable<PropertyValueDto>)null!);
            propValuesSource.GetPropertyValuesByName("KeyFillName").Returns(Array.Empty<PropertyValueDto>());
            propValuesSource.GetPropertyValuesByName("FallbackTarget").Returns(new[]
            {
                new PropertyValueDto { Id = 700, Value = "" }
            });

            var putBatch = new Dictionary<string, PropertyApiModel>
            {
                [""] = new PropertyApiModel { Name = "ByValName", Secure = true },
                ["KeyFillName"] = new PropertyApiModel { Name = "", Secure = true },
                ["OldKeyMiss"] = new PropertyApiModel { Name = "FallbackTarget", Secure = true },
                ["NullValueEntry"] = null!
            };
            var putBatchResults = editor.PutProperties(putBatch, _user).ToList();
            Assert.HasCount(4, putBatchResults);
            Assert.AreEqual("success", putBatchResults[0].Status);
            Assert.AreEqual("success", putBatchResults[1].Status);
            Assert.AreEqual("success", putBatchResults[2].Status);
            Assert.IsTrue(putBatchResults[3].Status.Contains("does not exist"));

            // EncryptExistingPropertyValues exception branch
            propsSource.GetProperty("EncryptBoom").Returns(new PropertyApiModel { Id = 23, Name = "EncryptBoom", Secure = false });
            propValuesSource.GetPropertyValuesByName("EncryptBoom").Returns(_ => throw new InvalidOperationException("db down"));
            var encBoomRes = editor.PutProperties(new Dictionary<string, PropertyApiModel>
            {
                ["EncryptBoom"] = new PropertyApiModel { Name = "EncryptBoom", Secure = true }
            }, _user).Single();
            Assert.IsTrue(encBoomRes.Status.Contains("error: failed to encrypt existing property values - db down"));

            // PutProperties general exception branch
            propsSource.GetProperty("PutBoom").Returns(_ => throw new InvalidOperationException("put boom"));
            var putBoomRes = editor.PutProperties(new Dictionary<string, PropertyApiModel>
            {
                ["PutBoom"] = new PropertyApiModel { Name = "PutBoom" }
            }, _user).Single();
            Assert.AreEqual("put boom", putBoomRes.Status);

            // 5. ImportProperties null/empty, conflicting IsSecure, and duplicate matching IsSecure with blank environment
            Assert.AreEqual(0, editor.ImportProperties(null!, _user).Count());
            Assert.AreEqual(0, editor.ImportProperties(Array.Empty<PropertyImportCsvDto>(), _user).Count());

            var conflictRes = editor.ImportProperties(new[]
            {
                new PropertyImportCsvDto { PropertyName = "DupProp", Value = "v1", IsSecure = false },
                new PropertyImportCsvDto { PropertyName = "DupProp", Value = "v2", IsSecure = true }
            }, _user).Single();
            Assert.IsTrue(conflictRes.Status.Contains("conflicting IsSecure"));

            pvs.PostPropertyValues(Arg.Any<IEnumerable<PropertyValueDto>>(), _user).Returns(Array.Empty<Response>());
            pvs.PutPropertyValues(Arg.Any<IEnumerable<PropertyValueDto>>(), _user).Returns(Array.Empty<Response>());
            var validDupRes = editor.ImportProperties(new[]
            {
                new PropertyImportCsvDto { PropertyName = "SameSecProp", Value = "v1", IsSecure = false, Environment = "" },
                new PropertyImportCsvDto { PropertyName = "SameSecProp", Value = "v2", IsSecure = false, Environment = "Env1" }
            }, _user).ToList();
            Assert.HasCount(1, validDupRes);
        }

        [TestMethod]
        public void PropertiesController_AllEndpointsAndAuthorizationBranches_AreCovered()
        {
            var editor = Substitute.For<IPropertyDefinitionEditor>();
            var security = Substitute.For<Dorc.Core.Interfaces.ISecurityPrivilegesChecker>();
            var roleChecker = Substitute.For<Dorc.PersistentData.IRolePrivilegesChecker>();
            var propValuesSource = Substitute.For<IPropertyValuesPersistentSource>();
            var envsSource = Substitute.For<IEnvironmentsPersistentSource>();

            var controller = new Dorc.Api.Controllers.PropertiesController(editor, security, roleChecker, propValuesSource, envsSource);

            var prop = new PropertyApiModel { Id = 1, Name = "Prop1", IsArray = true };
            editor.GetProperties().Returns(new[] { prop });
            Assert.AreEqual(1, controller.GetProperties().Count());

            editor.GetProperty("Prop1").Returns(prop);
            Assert.IsInstanceOfType(controller.GetProperty("Prop1"), typeof(Microsoft.AspNetCore.Mvc.OkObjectResult));

            editor.GetProperty("Missing").Returns((PropertyApiModel)null!);
            Assert.IsInstanceOfType(controller.GetProperty("Missing"), typeof(Microsoft.AspNetCore.Mvc.NotFoundResult));

            // PutProperty, DeleteProperties, ImportProperties when CanModifyProperty == false vs true
            security.CanModifyProperty(Arg.Any<ClaimsPrincipal>()).Returns(false);
            envsSource.GetEnvironments(Arg.Any<ClaimsPrincipal>()).Returns((IEnumerable<EnvironmentApiModel>)null!);

            Assert.AreEqual(403, ((Microsoft.AspNetCore.Mvc.ObjectResult)controller.PutProperty(new Dictionary<string, PropertyApiModel>())).StatusCode);
            Assert.AreEqual(403, ((Microsoft.AspNetCore.Mvc.ObjectResult)controller.PostProperties(new List<PropertyApiModel>())).StatusCode);
            Assert.AreEqual(403, ((Microsoft.AspNetCore.Mvc.ObjectResult)controller.DeleteProperties(new List<string>())).StatusCode);
            Assert.AreEqual(403, ((Microsoft.AspNetCore.Mvc.ObjectResult)controller.ImportProperties(new List<PropertyImportCsvDto>())).StatusCode);

            // PostProperties when CanModifyProperty == false, but GetEnvironments has non-editable env vs editable env
            envsSource.GetEnvironments(Arg.Any<ClaimsPrincipal>()).Returns(new[] { new EnvironmentApiModel { UserEditable = false } });
            Assert.AreEqual(403, ((Microsoft.AspNetCore.Mvc.ObjectResult)controller.PostProperties(new List<PropertyApiModel>())).StatusCode);

            envsSource.GetEnvironments(Arg.Any<ClaimsPrincipal>()).Returns(new[] { new EnvironmentApiModel { UserEditable = true } });
            Assert.IsInstanceOfType(controller.PostProperties(new List<PropertyApiModel>()), typeof(Microsoft.AspNetCore.Mvc.OkObjectResult));

            // Authorized via CanModifyProperty == true and IsAdmin == true
            security.CanModifyProperty(Arg.Any<ClaimsPrincipal>()).Returns(true);
            roleChecker.IsAdmin(Arg.Any<ClaimsPrincipal>()).Returns(true);
            Assert.IsInstanceOfType(controller.PutProperty(new Dictionary<string, PropertyApiModel>()), typeof(Microsoft.AspNetCore.Mvc.OkObjectResult));
            Assert.IsInstanceOfType(controller.PostProperties(new List<PropertyApiModel>()), typeof(Microsoft.AspNetCore.Mvc.OkObjectResult));
            Assert.IsInstanceOfType(controller.DeleteProperties(new List<string>()), typeof(Microsoft.AspNetCore.Mvc.OkObjectResult));
            Assert.IsInstanceOfType(controller.ImportProperties(new List<PropertyImportCsvDto>()), typeof(Microsoft.AspNetCore.Mvc.OkObjectResult));
        }

    }
}
