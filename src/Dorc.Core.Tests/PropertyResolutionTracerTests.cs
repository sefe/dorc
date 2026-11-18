using System.Security.Claims;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Interfaces;
using Dorc.Core.Lamar;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class PropertyResolutionTracerTests
    {
        private IPropertyValuesPersistentSource _propertyValuesSource = null!;
        private IVariableScopeOptionsResolver _scopeOptionsResolver = null!;
        private IConfigValuesPersistentSource _configValuesSource = null!;
        private ISecurityPrivilegesChecker _securityChecker = null!;
        private IPropertyEncryptor _propertyEncryptor = null!;
        private PropertyResolutionTracer _tracer = null!;
        private ClaimsPrincipal _user = null!;

        [TestInitialize]
        public void Setup()
        {
            _propertyValuesSource = Substitute.For<IPropertyValuesPersistentSource>();
            _scopeOptionsResolver = Substitute.For<IVariableScopeOptionsResolver>();
            _configValuesSource = Substitute.For<IConfigValuesPersistentSource>();
            _securityChecker = Substitute.For<ISecurityPrivilegesChecker>();
            _propertyEncryptor = Substitute.For<IPropertyEncryptor>();
            _user = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));

            _propertyValuesSource.GetGlobalProperties().Returns(Array.Empty<PropertyValueDto>());
            _propertyValuesSource.GetEnvironmentProperties(Arg.Any<string>(), Arg.Any<string>())
                .Returns(Array.Empty<PropertyValueDto>());
            _configValuesSource.GetAllConfigValues(false).Returns(Array.Empty<ConfigValueApiModel>());

            _tracer = new PropertyResolutionTracer(
                _propertyValuesSource,
                _scopeOptionsResolver,
                _configValuesSource,
                _securityChecker,
                _propertyEncryptor);
        }

        [TestMethod]
        public void TraceProperties_EmptyOrNullPropertyNames_ReturnsEmptyList()
        {
            var twoArgTracer = new PropertyResolutionTracer(_propertyValuesSource, _scopeOptionsResolver);
            Assert.AreEqual(0, twoArgTracer.TraceProperties(null!, null, _user).Count);
            Assert.AreEqual(0, twoArgTracer.TraceProperties(new PropertyResolutionPreviewRequestApiModel { PropertyNames = null! }, null, _user).Count);

            var request = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "DEV",
                ProjectName = "MyProject",
                PropertyNames = new List<string>()
            };

            var result = _tracer.TraceProperties(request, null, _user);

            Assert.AreEqual(0, result.Count);
            Assert.AreEqual("DEV", request.EnvironmentName);
            Assert.AreEqual("MyProject", request.ProjectName);
        }

        [TestMethod]
        public void TraceProperties_DeduplicatesExactNamesAndPreservesCaseSensitivity()
        {
            _propertyValuesSource.GetGlobalProperties().Returns(new[]
            {
                MakeProperty("PropA", "ValA")
            });

            var request = new PropertyResolutionPreviewRequestApiModel
            {
                PropertyNames = new List<string> { "  PropA ", "PropA", "propa", "", "   " }
            };

            var result = _tracer.TraceProperties(request, null, _user);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("PropA", result[0].PropertyName);
            Assert.AreEqual("ValA", result[0].RawValue);
            Assert.AreEqual("\"ValA\"", result[0].ResolvedValueJson);
            Assert.AreEqual("GlobalDefault", result[0].Source);
            Assert.AreEqual("Default", result[0].ScopeName);
            Assert.AreEqual("Ok", result[0].Diagnostic);
            Assert.AreEqual("String", result[0].ValueType);
            Assert.IsFalse(result[0].IsSecure);
            Assert.IsTrue(result[0].ResolutionChain.Count > 0);

            Assert.AreEqual("propa", result[1].PropertyName);
            Assert.AreEqual("Unresolved", result[1].Source);
            Assert.AreEqual("MissingInScope", result[1].Diagnostic);
        }

        [TestMethod]
        public void TraceProperties_GlobalFallbackInEnvironmentContext_MarksFallbackToDefault()
        {
            _propertyValuesSource.GetGlobalProperties().Returns(new[]
            {
                MakeProperty("GlobalOnly", "DefaultVal", isArray: true),
                MakeProperty("SecureGlobal", "CipherText", secure: true)
            });

            var env = new EnvironmentApiModel
            {
                EnvironmentName = "UAT-01",
                EnvironmentSecure = false,
                EnvironmentIsProd = false
            };

            var request = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "UAT-01",
                PropertyNames = new List<string> { "GlobalOnly", "SecureGlobal" }
            };

            var result = _tracer.TraceProperties(request, env, _user);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("GlobalDefault", result[0].Source);
            Assert.AreEqual("FallbackToDefault", result[0].Diagnostic);
            Assert.AreEqual("Array", result[0].ValueType);

            Assert.AreEqual("GlobalDefault", result[1].Source);
            Assert.IsTrue(result[1].IsSecure);
            Assert.AreEqual("[REDACTED]", result[1].RawValue);
            Assert.AreEqual("\"[REDACTED]\"", result[1].ResolvedValueJson);
        }

        [TestMethod]
        public void TraceProperties_SecureEnvironment_ExcludesGlobalDefaults()
        {
            _propertyValuesSource.GetGlobalProperties().Returns(new[]
            {
                MakeProperty("GlobalProp", "ShouldNotBeLoaded")
            });

            var env = new EnvironmentApiModel
            {
                EnvironmentName = "PROD-SEC",
                EnvironmentSecure = true,
                EnvironmentIsProd = true
            };

            var request = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "PROD-SEC",
                PropertyNames = new List<string> { "GlobalProp", "IsProd", "EnvironmentName" }
            };

            var result = _tracer.TraceProperties(request, env, _user);

            Assert.AreEqual("Unresolved", result[0].Source);
            Assert.AreEqual("MissingInScope", result[0].Diagnostic);
            Assert.IsNull(result[0].ScopeName);

            Assert.AreEqual("BuiltInScopeOption", result[1].Source);
            Assert.AreEqual("\"True\"", result[1].ResolvedValueJson);

            Assert.AreEqual("BuiltInScopeOption", result[2].Source);
            Assert.AreEqual("\"PROD-SEC\"", result[2].ResolvedValueJson);
        }

        [TestMethod]
        public void TraceProperties_EnvironmentAndAncestorScopes_PrecedenceAndClassification()
        {
            _propertyValuesSource.GetGlobalProperties().Returns(new[]
            {
                MakeProperty("ConnStr", "GlobalConn")
            });
            _propertyValuesSource.GetEnvironmentProperties("QA-CHILD", null).Returns(new[]
            {
                MakeProperty("ConnStr", "ParentConn", filter: "QA-PARENT"),
                MakeProperty("DirectProp", "ChildVal", filter: "QA-CHILD"),
                MakeProperty("NullFilterProp", @"[""plain-item-1"",""plain-item-2""]", filter: null, isArray: true),
                MakeProperty("SecureEnvProp", "Encrypted", filter: "QA-CHILD", secure: true)
            });

            var env = new EnvironmentApiModel { EnvironmentName = "QA-CHILD" };
            var request = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "QA-CHILD",
                PropertyNames = new List<string> { "ConnStr", "DirectProp", "NullFilterProp", "SecureEnvProp" }
            };

            var result = _tracer.TraceProperties(request, env, _user);

            Assert.AreEqual("AncestorEnvironmentScope", result[0].Source);
            Assert.AreEqual("QA-PARENT", result[0].ScopeName);
            Assert.AreEqual("Ok", result[0].Diagnostic);

            Assert.AreEqual("EnvironmentScope", result[1].Source);
            Assert.AreEqual("QA-CHILD", result[1].ScopeName);

            Assert.AreEqual("EnvironmentScope", result[2].Source);
            Assert.AreEqual("QA-CHILD", result[2].ScopeName);
            Assert.AreEqual("Array", result[2].ValueType);
            Assert.AreEqual(
                System.Text.Json.JsonSerializer.Serialize(@"[""plain-item-1"",""plain-item-2""]"),
                result[2].ResolvedValueJson);

            Assert.IsTrue(result[3].IsSecure);
            Assert.AreEqual("[REDACTED]", result[3].RawValue);
        }

        [TestMethod]
        public void TraceProperties_BuiltInScopeOptions_CapturesStringArrayAndComplexObjectsAndHandlesExceptions()
        {
            var serverObj = new VariableValueServers { Name = "srv-01.local", OsName = "Win2022" };
            _scopeOptionsResolver.When(r => r.SetPropertyValues(Arg.Any<IVariableResolver>(), Arg.Any<EnvironmentApiModel>()))
                .Do(call =>
                {
                    var resolver = call.Arg<IVariableResolver>();
                    _ = resolver.PropertiesLoaded();
                    _ = resolver.LocalProperties();
                    _ = resolver.LoadProperties();
                    _ = resolver.GetPropertyValue("Anything");
                    resolver.SetRequestSuppliedPropertyValue("EndurFileShare", @"\\files\share");
                    resolver.SetPropertyValue("ServerNames_App", new VariableValue
                    {
                        Value = new[] { "srv-01", "srv-02" },
                        Type = typeof(string[])
                    });
                    resolver.SetPropertyValue("AllServers", new VariableValue
                    {
                        Value = serverObj,
                        Type = typeof(VariableValueServers)
                    });
                    resolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwnerEmails, new VariableValue
                    {
                        Value = new[] { "owner1@dorc.local", "owner2@dorc.local" },
                        Type = typeof(string[])
                    });
                });

            var env = new EnvironmentApiModel { EnvironmentName = "DEV-01" };
            var request = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "DEV-01",
                PropertyNames = new List<string>
                {
                    "EndurFileShare",
                    "ServerNames_App",
                    "AllServers",
                    PropertyValueScopeOptionsFixed.EnvOwnerEmails
                }
            };

            var result = _tracer.TraceProperties(request, env, _user);

            Assert.AreEqual("BuiltInScopeOption", result[0].Source);
            Assert.AreEqual("String", result[0].ValueType);
            Assert.AreEqual(@"""\\\\files\\share""", result[0].ResolvedValueJson);

            Assert.AreEqual("BuiltInScopeOption", result[1].Source);
            Assert.AreEqual("String[]", result[1].ValueType);
            Assert.AreEqual(@"[""srv-01"",""srv-02""]", result[1].ResolvedValueJson);

            Assert.AreEqual("BuiltInScopeOption", result[2].Source);
            Assert.AreEqual("VariableValueServers", result[2].ValueType);
            StringAssert.Contains(result[2].ResolvedValueJson, "srv-01.local");

            Assert.AreEqual("BuiltInScopeOption", result[3].Source);
            Assert.AreEqual("String", result[3].ValueType);
            Assert.AreEqual(@"""owner1@dorc.local;owner2@dorc.local""", result[3].ResolvedValueJson);
        }

        [TestMethod]
        public void TraceProperties_ScopeOptionsResolverThrows_ContinuesGracefully()
        {
            _scopeOptionsResolver.When(r => r.SetPropertyValues(Arg.Any<IVariableResolver>(), Arg.Any<EnvironmentApiModel>()))
                .Do(_ => throw new InvalidOperationException("Duplicate server tag"));

            var env = new EnvironmentApiModel { EnvironmentName = "BROKEN-ENV" };
            var request = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "BROKEN-ENV",
                PropertyNames = new List<string> { "EnvironmentName" }
            };

            var result = _tracer.TraceProperties(request, env, _user);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("\"BROKEN-ENV\"", result[0].ResolvedValueJson);
        }

        [TestMethod]
        public void TraceProperties_MultiHopInterpolationAndEscapedDollars_ResolvesCorrectly()
        {
            var longText = new string('x', 100);
            _propertyValuesSource.GetGlobalProperties().Returns(new[]
            {
                MakeProperty("Host", "db.internal"),
                MakeProperty("Port", "1433"),
                MakeProperty("AliasToHost", "$Host$"),
                MakeProperty("JdbcUrl", "jdbc:sqlserver://$AliasToHost$:$Port$;password=$$literal$$"),
                MakeProperty("LongProp", longText)
            });

            var request = new PropertyResolutionPreviewRequestApiModel
            {
                PropertyNames = new List<string> { "JdbcUrl", "AliasToHost", "LongProp" }
            };

            var result = _tracer.TraceProperties(request, null, _user);

            Assert.AreEqual("\"jdbc:sqlserver://db.internal:1433;password=$literal$\"", result[0].ResolvedValueJson);
            Assert.AreEqual("Ok", result[0].Diagnostic);
            Assert.IsTrue(result[0].ResolutionChain.Count >= 3);

            Assert.AreEqual("\"db.internal\"", result[1].ResolvedValueJson);
            StringAssert.EndsWith(result[2].ResolutionChain[0], "...");
        }

        [TestMethod]
        public void TraceProperties_MultiHopFallbackInEnv_MarksFallbackDiagnosticOnSingleTokenAlias()
        {
            _propertyValuesSource.GetGlobalProperties().Returns(new[]
            {
                MakeProperty("GlobalTarget", "TargetVal"),
                MakeProperty("GlobalAlias", "$GlobalTarget$")
            });

            var env = new EnvironmentApiModel { EnvironmentName = "DEV-01" };
            var request = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "DEV-01",
                PropertyNames = new List<string> { "GlobalAlias" }
            };

            var result = _tracer.TraceProperties(request, env, _user);

            Assert.AreEqual("FallbackToDefault", result[0].Diagnostic);
            Assert.AreEqual("\"TargetVal\"", result[0].ResolvedValueJson);
        }

        [TestMethod]
        public void TraceProperties_SingleTokenAliasToNonString_PreservesChildType()
        {
            _scopeOptionsResolver.When(r => r.SetPropertyValues(Arg.Any<IVariableResolver>(), Arg.Any<EnvironmentApiModel>()))
                .Do(call =>
                {
                    call.Arg<IVariableResolver>().SetPropertyValue("ServerNames_Web", new VariableValue
                    {
                        Value = new[] { "web-1" },
                        Type = typeof(string[])
                    });
                });

            _propertyValuesSource.GetEnvironmentProperties("DEV-01", null).Returns(new[]
            {
                MakeProperty("MyServers", "$ServerNames_Web$", filter: "DEV-01"),
                MakeProperty("ConcatWithArray", "prefix-$ServerNames_Web$", filter: "DEV-01")
            });

            var env = new EnvironmentApiModel { EnvironmentName = "DEV-01" };
            var request = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "DEV-01",
                PropertyNames = new List<string> { "MyServers", "ConcatWithArray" }
            };

            var result = _tracer.TraceProperties(request, env, _user);

            Assert.AreEqual("String[]", result[0].ValueType);
            Assert.AreEqual(@"[""web-1""]", result[0].ResolvedValueJson);

            Assert.AreEqual("NonStringInConcatenation", result[1].Diagnostic);
        }

        [TestMethod]
        public void TraceProperties_TransitiveSecretInInterpolation_RedactsEntireValue()
        {
            _propertyValuesSource.GetGlobalProperties().Returns(new[]
            {
                MakeProperty("DbPassword", "SuperSecret", secure: true),
                MakeProperty("ConnectionString", "Server=myserver;Password=$DbPassword$;")
            });

            var request = new PropertyResolutionPreviewRequestApiModel
            {
                PropertyNames = new List<string> { "ConnectionString" }
            };

            var result = _tracer.TraceProperties(request, null, _user);

            Assert.IsTrue(result[0].IsSecure);
            Assert.AreEqual("[REDACTED]", result[0].RawValue);
            Assert.AreEqual("\"[REDACTED]\"", result[0].ResolvedValueJson);
        }

        [TestMethod]
        public void TraceProperties_CircularReferenceAndDepthCap_DetectsCycleWithoutThrowing()
        {
            var props = new List<PropertyValueDto>
            {
                MakeProperty("CycleA", "$CycleB$"),
                MakeProperty("CycleB", "prefix-$CycleA$")
            };

            for (var i = 0; i <= 35; i++)
            {
                props.Add(MakeProperty($"Hop{i}", $"$Hop{i + 1}$"));
            }

            _propertyValuesSource.GetGlobalProperties().Returns(props.ToArray());

            var request = new PropertyResolutionPreviewRequestApiModel
            {
                PropertyNames = new List<string> { "CycleA", "Hop0" }
            };

            var result = _tracer.TraceProperties(request, null, _user);

            Assert.AreEqual("CircularReference", result[0].Diagnostic);
            Assert.IsNull(result[0].ResolvedValueJson);

            Assert.AreEqual("CircularReference", result[1].Diagnostic);
            Assert.IsNull(result[1].ResolvedValueJson);
        }

        [TestMethod]
        public void TraceProperties_ExpressionsAndRequestOverrides_DefersOrBlocksExecution()
        {
            _propertyValuesSource.GetGlobalProperties().Returns(new[]
            {
                MakeProperty("SafeExpr", "fn:System.DateTime.UtcNow.ToString()"),
                MakeProperty("ConcatSafeExpr", "fn:1 + $NormalProp$"),
                MakeProperty("NormalProp", "2"),
                MakeProperty("OverriddenProp", "SafeDefault"),
                MakeProperty("DependentExpr", "fn:Use($OverriddenProp$)"),
                MakeProperty("WrapperAroundExpr", "Result=$SafeExpr$"),
                MakeProperty("ExistingSecret", "Encrypted", secure: true)
            });

            var request = new PropertyResolutionPreviewRequestApiModel
            {
                PropertyNames = new List<string>
                {
                    "SafeExpr",
                    "ConcatSafeExpr",
                    "DependentExpr",
                    "WrapperAroundExpr",
                    "DirectOverrideExpr",
                    "SensitiveOverride",
                    "ExistingSecret"
                },
                RequestOverrides = new List<RequestProperty>
                {
                    new RequestProperty { PropertyName = "   ", PropertyValue = "Ignored" },
                    new RequestProperty { PropertyName = "OverriddenProp", PropertyValue = "Injected" },
                    new RequestProperty { PropertyName = "DirectOverrideExpr", PropertyValue = "fn:Evil()" },
                    new RequestProperty { PropertyName = "SensitiveOverride", PropertyValue = "TopSecret", IsSensitive = true },
                    new RequestProperty { PropertyName = "ExistingSecret", PropertyValue = "NewSecret", IsSensitive = false }
                }
            };

            var result = _tracer.TraceProperties(request, null, _user);

            Assert.AreEqual("ExpressionDeferred", result[0].Diagnostic);
            Assert.AreEqual("ExpressionDeferred", result[1].Diagnostic);
            Assert.AreEqual("ExpressionBlockedByOverride", result[2].Diagnostic);
            Assert.AreEqual("ExpressionDeferred", result[3].Diagnostic);
            Assert.AreEqual("ExpressionBlockedByOverride", result[4].Diagnostic);
            Assert.AreEqual("RequestOverride", result[4].Source);

            Assert.IsTrue(result[5].IsSecure);
            Assert.AreEqual("[REDACTED]", result[5].RawValue);

            Assert.IsTrue(result[6].IsSecure);
            Assert.AreEqual("[REDACTED]", result[6].RawValue);
        }

        [TestMethod]
        public void TraceProperties_RuntimeOnlyAndUnclosedAndUnresolvedDependencies_ReportsDiagnostics()
        {
            _propertyValuesSource.GetGlobalProperties().Returns(new[]
            {
                MakeProperty("BrokenSyntax", "Hello $UnclosedToken"),
                MakeProperty("UsesMissing", "Path=$NonExistent$/bin"),
                MakeProperty("UsesRuntime", "$ScriptRoot$/deploy.ps1")
            });

            var request = new PropertyResolutionPreviewRequestApiModel
            {
                PropertyNames = new List<string>
                {
                    "ScriptRoot",
                    "NonExistent",
                    "BrokenSyntax",
                    "UsesMissing",
                    "UsesRuntime"
                }
            };

            var result = _tracer.TraceProperties(request, null, _user);

            Assert.AreEqual("RuntimeOnly", result[0].Source);
            Assert.AreEqual("RuntimeOnly", result[0].Diagnostic);

            Assert.AreEqual("Unresolved", result[1].Source);
            Assert.AreEqual("MissingInScope", result[1].Diagnostic);

            Assert.AreEqual("UnclosedToken", result[2].Diagnostic);
            Assert.AreEqual("\"Hello $UnclosedToken\"", result[2].ResolvedValueJson);

            Assert.AreEqual("MissingInScope", result[3].Diagnostic);
            Assert.AreEqual("\"Path=$NonExistent$/bin\"", result[3].ResolvedValueJson);

            Assert.AreEqual("RuntimeOnly", result[4].Diagnostic);
            Assert.AreEqual("\"$ScriptRoot$/deploy.ps1\"", result[4].ResolvedValueJson);
        }

        [TestMethod]
        public void TraceProperties_ReadSecretsPermissionAndConfigValueFallbacks_BehavesCorrectly()
        {
            _propertyValuesSource.GetGlobalProperties().Returns(new[]
            {
                MakeProperty("GlobalSec", "EncGlobal", secure: true),
                MakeProperty("GlobalSecArray", "[\"EncArr1\",\"EncArr2\"]", secure: true, isArray: true),
                MakeProperty("ExistingProp", "FromGlobal"),
                MakeProperty("BuildNumber", "StoredBuildToIgnore")
            });
            _propertyValuesSource.GetEnvironmentProperties("QA-CHILD", null).Returns(new[]
            {
                MakeProperty("DirectSec", "EncDirect", filter: "QA-CHILD", secure: true),
                MakeProperty("DirectSecWithTokens", "EncSecretWithTokens", filter: "QA-CHILD", secure: true),
                MakeProperty("AncestorSecAllowed", "EncAncestorOk", filter: "QA-PARENT", secure: true),
                MakeProperty("AncestorSecDenied", "EncAncestorNo", filter: "QA-LOCKED-PARENT", secure: true),
                MakeProperty("UsesDecryptedSecret", "conn=$DirectSec$", filter: "QA-CHILD")
            });

            _configValuesSource.GetAllConfigValues(false).Returns(new[]
            {
                new ConfigValueApiModel { Key = "ExistingProp", Value = "ConfigIgnored", IsForProd = null, Secure = false },
                new ConfigValueApiModel { Key = "AnyEnvConfig", Value = "AnyVal", IsForProd = null, Secure = false },
                new ConfigValueApiModel { Key = "NonProdConfig", Value = "NonProdVal", IsForProd = false, Secure = false },
                new ConfigValueApiModel { Key = "ProdOnlyConfig", Value = "ProdVal", IsForProd = true, Secure = false },
                new ConfigValueApiModel { Key = "SecureConfig", Value = "EncryptedCfg", IsForProd = null, Secure = true }
            });

            _securityChecker.CanReadSecrets(_user, "QA-CHILD").Returns(true);
            _securityChecker.CanReadSecrets(_user, "QA-PARENT").Returns(true);
            _securityChecker.CanReadSecrets(_user, "QA-LOCKED-PARENT").Returns(false);

            _propertyEncryptor.DecryptValue("EncGlobal").Returns("PlainGlobal");
            _propertyEncryptor.DecryptValue("EncArr1").Returns("PlainArr1");
            _propertyEncryptor.DecryptValue("EncArr2").Returns("PlainArr2");
            _propertyEncryptor.DecryptValue("EncDirect").Returns("PlainDirect");
            _propertyEncryptor.DecryptValue("EncSecretWithTokens").Returns("fn:$ExistingProp$$$unclosed$");
            _propertyEncryptor.DecryptValue("EncAncestorOk").Returns("PlainAncestor");

            var env = new EnvironmentApiModel
            {
                EnvironmentName = "QA-CHILD",
                EnvironmentIsProd = false
            };

            var req = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "QA-CHILD",
                PropertyNames = new List<string>
                {
                    "GlobalSec",
                    "DirectSec",
                    "AncestorSecAllowed",
                    "AncestorSecDenied",
                    "UsesDecryptedSecret",
                    "ExistingProp",
                    "AnyEnvConfig",
                    "NonProdConfig",
                    "ProdOnlyConfig",
                    "SecureConfig",
                    "GlobalSecArray",
                    "DirectSecWithTokens",
                    "BuildNumber",
                    "DropFolder"
                },
                RequestOverrides = new List<RequestProperty>
                {
                    new RequestProperty { PropertyName = "DropFolder", PropertyValue = @"\\custom\drop" }
                }
            };

            var res = _tracer.TraceProperties(req, env, _user);

            // GlobalSec decrypted because user has ReadSecrets on target QA-CHILD
            Assert.IsTrue(res[0].IsSecure);
            Assert.AreEqual("\"PlainGlobal\"", res[0].ResolvedValueJson);

            // DirectSec decrypted
            Assert.IsTrue(res[1].IsSecure);
            Assert.AreEqual("\"PlainDirect\"", res[1].ResolvedValueJson);

            // AncestorSecAllowed decrypted because user has ReadSecrets on both QA-CHILD and QA-PARENT
            Assert.IsTrue(res[2].IsSecure);
            Assert.AreEqual("\"PlainAncestor\"", res[2].ResolvedValueJson);

            // AncestorSecDenied redacted because user lacks ReadSecrets on QA-LOCKED-PARENT
            Assert.IsTrue(res[3].IsSecure);
            Assert.AreEqual("\"[REDACTED]\"", res[3].ResolvedValueJson);

            // UsesDecryptedSecret interpolates PlainDirect and inherits IsSecure = true
            Assert.IsTrue(res[4].IsSecure);
            Assert.AreEqual("\"conn=PlainDirect\"", res[4].ResolvedValueJson);

            // ExistingProp keeps GlobalDefault, not overwritten by ConfigValue
            Assert.AreEqual("GlobalDefault", res[5].Source);
            Assert.AreEqual("\"FromGlobal\"", res[5].ResolvedValueJson);

            // AnyEnvConfig and NonProdConfig included for non-prod environment
            Assert.AreEqual("ConfigValue", res[6].Source);
            Assert.AreEqual("\"AnyVal\"", res[6].ResolvedValueJson);
            Assert.AreEqual("ConfigValue", res[7].Source);
            Assert.AreEqual("\"NonProdVal\"", res[7].ResolvedValueJson);

            // ProdOnlyConfig excluded in non-prod environment
            Assert.AreEqual("Unresolved", res[8].Source);

            // SecureConfig is redacted
            Assert.AreEqual("ConfigValue", res[9].Source);
            Assert.IsTrue(res[9].IsSecure);
            Assert.AreEqual("\"[REDACTED]\"", res[9].ResolvedValueJson);

            // GlobalSecArray decrypts each element without throwing
            Assert.IsTrue(res[10].IsSecure);
            Assert.AreEqual("Array", res[10].ValueType);
            Assert.AreEqual("[\"PlainArr1\",\"PlainArr2\"]", res[10].RawValue);

            // DirectSecWithTokens returns literal decrypted string without parsing $ExistingProp$, $$, or fn:
            Assert.IsTrue(res[11].IsSecure);
            Assert.AreEqual("Ok", res[11].Diagnostic);
            Assert.AreEqual("\"fn:$ExistingProp$$$unclosed$\"", res[11].ResolvedValueJson);

            // Stored BuildNumber is ignored in favor of RuntimeOnly when not overridden
            Assert.AreEqual("RuntimeOnly", res[12].Source);
            Assert.AreEqual("RuntimeOnly", res[12].Diagnostic);

            // Overridden DropFolder preserves RequestOverride
            Assert.AreEqual("RequestOverride", res[13].Source);
            Assert.AreEqual(@"""\\\\custom\\drop""", res[13].ResolvedValueJson);

            // Also verify prod environment includes IsForProd = true and excludes IsForProd = false,
            // and verify tracer without optional dependencies (null checker / encryptor / config source)
            var prodEnv = new EnvironmentApiModel
            {
                EnvironmentName = "PROD-01",
                EnvironmentIsProd = true
            };
            _securityChecker.CanReadSecrets(_user, "PROD-01").Returns(true);
            var prodRes = _tracer.TraceProperties(new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "PROD-01",
                PropertyNames = new List<string> { "ProdOnlyConfig", "NonProdConfig" }
            }, prodEnv, _user);
            Assert.AreEqual("ConfigValue", prodRes[0].Source);
            Assert.AreEqual("Unresolved", prodRes[1].Source);

            var noDepsTracer = new PropertyResolutionTracer(
                _propertyValuesSource,
                _scopeOptionsResolver,
                null,
                null,
                null);
            var noEncryptorRes = noDepsTracer.TraceProperties(new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "QA-CHILD",
                PropertyNames = new List<string> { "GlobalSec", "DirectSec", "DirectSec", "AncestorSecAllowed" }
            }, env, _user);
            Assert.AreEqual("\"[REDACTED]\"", noEncryptorRes[0].ResolvedValueJson);
            Assert.AreEqual("\"[REDACTED]\"", noEncryptorRes[1].ResolvedValueJson);
            Assert.AreEqual("\"[REDACTED]\"", noEncryptorRes[2].ResolvedValueJson);

            var checkerOnlyTracer = new PropertyResolutionTracer(
                _propertyValuesSource,
                _scopeOptionsResolver,
                null,
                _securityChecker,
                null);
            var checkerOnlyRes = checkerOnlyTracer.TraceProperties(new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "QA-CHILD",
                PropertyNames = new List<string> { "GlobalSec", "DirectSec" }
            }, env, _user);
            Assert.AreEqual("\"[REDACTED]\"", checkerOnlyRes[0].ResolvedValueJson);
            Assert.AreEqual("\"[REDACTED]\"", checkerOnlyRes[1].ResolvedValueJson);
        }

        [TestMethod]
        public void TraceProperties_DiamondDag_ReusesEvaluatedCacheAcrossBranches()
        {
            _scopeOptionsResolver.When(r => r.SetPropertyValues(Arg.Any<IVariableResolver>(), Arg.Any<EnvironmentApiModel>()))
                .Do(call =>
                {
                    call.Arg<IVariableResolver>().SetPropertyValue("NonStringScope", new VariableValue
                    {
                        Value = new[] { "srv1" },
                        Type = typeof(string[])
                    });
                });

            _securityChecker.CanReadSecrets(_user, "DEV-01").Returns(true);
            _propertyValuesSource.GetGlobalProperties().Returns(new[]
            {
                MakeProperty("Leaf", "val"),
                MakeProperty("Branch1", "$Leaf$-$Leaf$"),
                MakeProperty("Branch2", "$Branch1$:$Branch1$"),
                MakeProperty("Root", "$Branch2$|$Branch2$"),
                MakeProperty("ExprMissingRef", "fn:Call($UndefinedRef$)"),
                MakeProperty("ExprWithMalformedChild", "fn:Call($MalformedRef$)"),
                MakeProperty("MalformedRef", "$Leaf"),
                MakeProperty("TransitiveMissingParent", "prefix-$ExprMissingRef$"),
                MakeProperty("NullSecureArray", "null", secure: true, isArray: true),
                MakeProperty("CorruptSecureArray", "{not-json", secure: true, isArray: true),
                MakeProperty("CorruptPlainArray", "{not-json", secure: false, isArray: true),
                MakeProperty("HugeChunk", new string('a', 40_000)),
                MakeProperty("HugeConcat", "$HugeChunk$-$HugeChunk$-$Leaf$"),
                MakeProperty("SecretWithAbsentToken", "$AbsentToken$", secure: true),
                MakeProperty("ExprUsingSecretWithAbsentToken", "fn:Call($SecretWithAbsentToken$)"),
                MakeProperty("PlainWithAbsentToken", "$$AbsentToken$$", secure: false),
                MakeProperty("ExprDirectAbsentToken", "fn:Call($PlainWithAbsentToken$)"),
                MakeProperty("AliasToIsProd", "$IsProd$"),
                MakeProperty("BadFormatSecret", "bad-base64", secure: true),
                MakeProperty("BadInvalidOpSecret", "bad-op", secure: true),
                MakeProperty("BadCryptoSecret", "bad-crypto", secure: true)
            });
            _propertyEncryptor.When(e => e.DecryptValue("bad-base64")).Do(_ => throw new FormatException("bad format"));
            _propertyEncryptor.When(e => e.DecryptValue("bad-op")).Do(_ => throw new InvalidOperationException("bad op"));
            _propertyEncryptor.When(e => e.DecryptValue("bad-crypto")).Do(_ => throw new System.Security.Cryptography.CryptographicException("bad crypto"));

            _propertyValuesSource.GetEnvironmentProperties("DEV-01", null).Returns(new[]
            {
                MakeProperty("AncestorSecret1", "cipher1", filter: "PARENT-01", secure: true),
                MakeProperty("AncestorSecret2", "cipher2", filter: "PARENT-01", secure: true)
            });
            _securityChecker.CanReadSecrets(_user, "PARENT-01").Returns(true);

            var env = new EnvironmentApiModel { EnvironmentName = "DEV-01" };
            var req = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "DEV-01",
                PropertyNames = new List<string>
                {
                    "Root", "Leaf", "ExprMissingRef", "ExprWithMalformedChild",
                    "TransitiveMissingParent", "NullSecureArray", "CorruptSecureArray",
                    "HugeConcat", "AncestorSecret1", "AncestorSecret2", "CorruptPlainArray",
                    "ExprUsingSecretWithAbsentToken", "ExprDirectAbsentToken",
                    "IsProd", "AliasToIsProd", "BadFormatSecret", "BadInvalidOpSecret", "BadCryptoSecret"
                },
                RequestOverrides = new List<RequestProperty>
                {
                    new RequestProperty { PropertyName = "OtherOverride", PropertyValue = new string('x', 70_000) }
                }
            };

            var res = _tracer.TraceProperties(req, env, _user);
            Assert.AreEqual("\"val-val:val-val|val-val:val-val\"", res[0].ResolvedValueJson);
            Assert.AreEqual("\"val\"", res[1].ResolvedValueJson);
            Assert.AreEqual("MissingInScope", res[2].Diagnostic);
            Assert.AreEqual("UnclosedToken", res[3].Diagnostic);
            Assert.AreEqual("MissingInScope", res[4].Diagnostic);
            Assert.AreEqual("\"prefix-fn:Call($UndefinedRef$)\"", res[4].ResolvedValueJson);
            Assert.AreEqual("null", res[5].RawValue);
            Assert.AreEqual("[REDACTED]", res[6].RawValue);
            Assert.AreEqual("ValueTruncated", res[7].Diagnostic);
            Assert.AreEqual("{not-json", res[10].RawValue);
            Assert.AreEqual("ExpressionDeferred", res[11].Diagnostic);
            Assert.AreEqual("ExpressionDeferred", res[12].Diagnostic);
            Assert.AreEqual("\"False\"", res[13].ResolvedValueJson);
            Assert.AreEqual("BuiltInScopeOption", res[13].Source);
            Assert.AreEqual("\"$IsProd$\"", res[14].ResolvedValueJson);
            Assert.AreEqual("MissingInScope", res[14].Diagnostic);
            Assert.AreEqual("[REDACTED]", res[15].RawValue);
            Assert.AreEqual("[REDACTED]", res[16].RawValue);
            Assert.AreEqual("[REDACTED]", res[17].RawValue);
            _securityChecker.Received(1).CanReadSecrets(_user, "PARENT-01");

            var noEnvIsProdRes = _tracer.TraceProperties(new PropertyResolutionPreviewRequestApiModel
            {
                PropertyNames = new List<string> { "IsProd" }
            }, null, _user);
            Assert.AreEqual("Unresolved", noEnvIsProdRes[0].Source);

            var overriddenIsProdRes = _tracer.TraceProperties(new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "DEV-01",
                PropertyNames = new List<string> { "IsProd" },
                RequestOverrides = new List<RequestProperty>
                {
                    new RequestProperty { PropertyName = "IsProd", PropertyValue = "CustomIsProd" }
                }
            }, env, _user);
            Assert.AreEqual("\"CustomIsProd\"", overriddenIsProdRes[0].ResolvedValueJson);
            Assert.AreEqual("RequestOverride", overriddenIsProdRes[0].Source);
        }

        [TestMethod]
        public void CoreRegistry_RegistersPropertyResolutionTracer()
        {
            var registry = new CoreRegistry();
            Assert.IsTrue(registry.Any(sd => sd.ServiceType == typeof(IPropertyResolutionTracer)));
        }

        [TestMethod]
        public void PropertyParser_HandlesWindowsAdminSharesAndBraceTokensGracefully()
        {
            var parser = new PropertyParser();

            // Single drive share + subsequent $Token$
            var tokens1 = parser.Parse(@"\\$ServerNames_App$\E$\Apps\$OtherVar$").ToList();
            Assert.HasCount(4, tokens1);
            Assert.IsInstanceOfType<StaticToken>(tokens1[0]);
            Assert.AreEqual(@"\\", ((StaticToken)tokens1[0]).Value);
            Assert.IsInstanceOfType<PropertyToken>(tokens1[1]);
            Assert.AreEqual("ServerNames_App", ((PropertyToken)tokens1[1]).Value);
            Assert.IsInstanceOfType<StaticToken>(tokens1[2]);
            Assert.AreEqual(@"\E$\Apps\", ((StaticToken)tokens1[2]).Value);
            Assert.IsInstanceOfType<PropertyToken>(tokens1[3]);
            Assert.AreEqual("OtherVar", ((PropertyToken)tokens1[3]).Value);

            // Two admin shares in one path
            var tokens2 = parser.Parse(@"\\srv\C$\x\D$\y").ToList();
            Assert.HasCount(1, tokens2);
            Assert.IsInstanceOfType<StaticToken>(tokens2[0]);
            Assert.AreEqual(@"\\srv\C$\x\D$\y", ((StaticToken)tokens2[0]).Value);

            // ADMIN$ and IPC$ shares
            var tokens3 = parser.Parse(@"\\srv\ADMIN$\System32\IPC$").ToList();
            Assert.HasCount(1, tokens3);
            Assert.AreEqual(@"\\srv\ADMIN$\System32\IPC$", ((StaticToken)tokens3[0]).Value);
        }

        private static PropertyValueDto MakeProperty(
            string name,
            string val,
            string? filter = null,
            bool secure = false,
            bool isArray = false)
        {
            return new PropertyValueDto
            {
                Value = val,
                PropertyValueFilter = filter,
                Property = new PropertyApiModel
                {
                    Name = name,
                    Secure = secure,
                    IsArray = isArray
                }
            };
        }
    }
}
