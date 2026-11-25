using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class VariableResolverTests
    {
        private IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private ILoggerFactory _loggerFactory;
        private VariableResolver _resolver;

        [TestInitialize]
        public void Setup()
        {
            _propertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            _loggerFactory = Substitute.For<ILoggerFactory>();
            _loggerFactory.CreateLogger<PropertyExpressionEvaluator>().Returns(Substitute.For<ILogger<PropertyExpressionEvaluator>>());
            _resolver = new VariableResolver(_propertyValuesPersistentSource, _loggerFactory, new PropertyEvaluator());
        }

        [TestMethod]
        public void GetPropertyValue_ReturnsPlainValue()
        {
            var propertyName = "plainProp";
            var value = new VariableValue { Value = "plainValue", Type = typeof(string) };
            _resolver.SetPropertyValue(propertyName, value);
            _propertyValuesPersistentSource.IsCachedPropertySecure(propertyName).Returns(false);
            var result = _resolver.GetPropertyValue(propertyName);
            Assert.IsNotNull(result);
            Assert.AreEqual("plainValue", result.Value);
        }

        [TestMethod]
        public void GetPropertyValue_EvaluatesPropertyReference()
        {
            // prop1: 'abc', prop2: '$prop1$_123' => 'abc_123'
            var prop1 = "prop1";
            var prop2 = "prop2";
            var value1 = new VariableValue { Value = "abc", Type = typeof(string) };
            var value2 = new VariableValue { Value = "$prop1$_123", Type = typeof(string) };
            var evaluated2 = new VariableValue { Value = "abc_123", Type = typeof(string) };
            _resolver.SetPropertyValue(prop1, value1);
            _resolver.SetPropertyValue(prop2, value2);
            _propertyValuesPersistentSource.IsCachedPropertySecure(Arg.Any<string>()).Returns(false);
            var result = _resolver.GetPropertyValue(prop2);
            Assert.AreEqual("abc_123", result.Value);
        }

        [TestMethod]
        public void GetPropertyValue_EvaluatesExpression()
        {
            // prop1: 'abc', prop2: 'fn:"$prop1$".ToUpper()' => 'ABC'
            var prop1 = "prop1";
            var prop2 = "prop2";
            var value1 = new VariableValue { Value = "abc", Type = typeof(string) };
            var value2 = new VariableValue { Value = "fn:\"$prop1$\".ToUpper()", Type = typeof(string) };
            var evaluated2 = new VariableValue { Value = "fn:\"$prop1$\".ToUpper()", Type = typeof(string) };
            _resolver.SetPropertyValue(prop1, value1);
            _resolver.SetPropertyValue(prop2, value2);
            _propertyValuesPersistentSource.IsCachedPropertySecure(Arg.Any<string>()).Returns(false);
            
            var result = _resolver.GetPropertyValue(prop2);
            Assert.IsNotNull(result);
            Assert.AreEqual("ABC", result.Value);
        }

        [TestMethod]
        public void GetPropertyValue_ReturnsSecureValue_WithoutEvaluation()
        {
            var propertyName = "secureProp";
            var secureValue = new VariableValue { Value = "fn:1+1", Type = typeof(string) };
            _resolver.SetPropertyValue(propertyName, secureValue);
            _propertyValuesPersistentSource.IsCachedPropertySecure(
                Arg.Is<string>(s => string.Equals(s, propertyName, StringComparison.OrdinalIgnoreCase))).Returns(true);
            var result = _resolver.GetPropertyValue("SECUREPROP");
            Assert.IsNotNull(result);
            Assert.AreEqual(secureValue.Value, result.Value);
        }

        [TestMethod]
        public void LoadProperties_SkipsBadExpressionsAndLoadsGoodOnes()
        {
            // prop1: 'abc', prop2: 'fn:bad(' (bad expression), prop3: '$prop1$_xyz' (should be 'abc_xyz')
            var prop1 = "prop1";
            var prop2 = "prop2";
            var prop3 = "prop3";
            var value1 = new PropertyValueDto { Value = "abc" };
            var value2 = new PropertyValueDto { Value = "fn:bad(" };
            var value3 = new PropertyValueDto { Value = "$prop1$_xyz" };
            var properties = new Dictionary<string, PropertyValueDto>
            {
                { prop1, value1 },
                { prop2, value2 },
                { prop3, value3 }
            };
            _propertyValuesPersistentSource.LoadAllPropertiesIntoCache().Returns(properties);
            _propertyValuesPersistentSource.IsCachedPropertySecure(Arg.Any<string>()).Returns(false);

            // Act
            IDictionary<string, VariableValue> result = null;
            result = _resolver.LoadProperties();
            
            Assert.IsNotNull(result, "LoadProperties should not throw, but return a dictionary");
            Assert.IsTrue(result.ContainsKey(prop1), "prop1 should be loaded");
            Assert.IsTrue(result.ContainsKey(prop3), "prop3 should be loaded");
            Assert.IsFalse(result.ContainsKey(prop2), "Bad expression property should be skipped");
            Assert.AreEqual("abc_xyz", result[prop3].Value);
        }

        [TestMethod]
        public void GetPropertyValue_CaseInsensitiveCircularReference_DoesNotStackOverflow()
        {
            _resolver.SetPropertyValue("PropA", "$propb$");
            _resolver.SetPropertyValue("PropB", "$PROPA$");
            _propertyValuesPersistentSource.IsCachedPropertySecure(Arg.Any<string>()).Returns(false);

            var emptyKeyResult = _resolver.GetPropertyValue(string.Empty);
            Assert.IsNull(emptyKeyResult);

            var result = _resolver.GetPropertyValue("propa");
            Assert.IsNotNull(result);
            Assert.AreEqual("$PROPA$", result.Value);

            // Also verify repository-backed RawValueOf provenance lookup when not in localProperties
            _resolver.SetRequestSuppliedPropertyValue("ReqInput", "injected");
            _propertyValuesPersistentSource.GetCachedPropertyValue("RepoExpr")
                .Returns(new PropertyValueDto
                {
                    Value = "fn:\"$reqinput$\"",
                    Property = new PropertyApiModel { Name = "RepoExpr", Secure = false }
                });
            var repoResult = _resolver.GetPropertyValue("RepoExpr");
            Assert.IsNotNull(repoResult);
            Assert.AreEqual("fn:\"injected\"", repoResult.Value);

            // Also verify BundledRequestVariableResolver cycle guard
            var loader = Substitute.For<IBundledRequestVariableLoader>();
            loader.GetVariables().Returns(new List<RequestProperty>
            {
                new() { PropertyName = "Cycle1", PropertyValue = "$cycle2$" },
                new() { PropertyName = "Cycle2", PropertyValue = "$CYCLE1$" }
            });
            var exprEval = Substitute.For<IPropertyExpressionEvaluator>();
            exprEval.Evaluate(Arg.Any<object>()).Returns(ci => ci.Arg<object>());
            var bundledResolver = new BundledRequestVariableResolver(loader, exprEval, new PropertyEvaluator());
            Assert.IsNull(bundledResolver.GetPropertyValue(string.Empty));
            var bundledDict = bundledResolver.LoadProperties();
            Assert.HasCount(2, bundledDict);
            Assert.AreEqual("$CYCLE1$", bundledDict["Cycle1"].Value);
        }

        [TestMethod]
        public void GetPropertyValue_ResolvesArrayPropertiesAndInterpolatesElements()
        {
            var properties = new Dictionary<string, PropertyValueDto>
            {
                ["EnvTag"] = new PropertyValueDto
                {
                    Value = "dv01",
                    Property = new PropertyApiModel { Name = "EnvTag", Secure = false, IsArray = false }
                },
                ["BaseHosts"] = new PropertyValueDto
                {
                    Value = "[\"host1.$EnvTag$.local\",\"host2.$EnvTag$.local\"]",
                    Property = new PropertyApiModel { Name = "BaseHosts", Secure = false, IsArray = true }
                },
                ["ExpandedHosts"] = new PropertyValueDto
                {
                    Value = "[\"$BaseHosts$\",\"host3.$EnvTag$.local\"]",
                    Property = new PropertyApiModel { Name = "ExpandedHosts", Secure = false, IsArray = true }
                },
                ["SecureTokens"] = new PropertyValueDto
                {
                    Value = "[\"sec-$EnvTag$-1\",\"fn:1+1\"]",
                    Property = new PropertyApiModel { Name = "SecureTokens", Secure = true, IsArray = true }
                },
                ["SecureScalarFallback"] = new PropertyValueDto
                {
                    Value = "raw-secret-element",
                    Property = new PropertyApiModel { Name = "SecureScalarFallback", Secure = true, IsArray = true }
                },
                ["PlainScalarFallback"] = new PropertyValueDto
                {
                    Value = "node-$EnvTag$",
                    Property = new PropertyApiModel { Name = "PlainScalarFallback", Secure = false, IsArray = true }
                },
                ["AliasedArray"] = new PropertyValueDto
                {
                    Value = "$BaseHosts$",
                    Property = new PropertyApiModel { Name = "AliasedArray", Secure = false, IsArray = true }
                },
                ["MalformedJsonArray"] = new PropertyValueDto
                {
                    Value = "[not-valid-json",
                    Property = new PropertyApiModel { Name = "MalformedJsonArray", Secure = false, IsArray = true }
                },
                ["JoinedSummary"] = new PropertyValueDto
                {
                    Value = "hosts=$BaseHosts$!",
                    Property = new PropertyApiModel { Name = "JoinedSummary", Secure = false, IsArray = false }
                }
            };

            _propertyValuesPersistentSource.LoadAllPropertiesIntoCache().Returns(properties);
            _propertyValuesPersistentSource.IsCachedPropertySecure(Arg.Is<string>(s =>
                s.Equals("SecureTokens", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("SecureScalarFallback", StringComparison.OrdinalIgnoreCase))).Returns(true);
            _propertyValuesPersistentSource.IsCachedPropertyArray(Arg.Is<string>(s =>
                s.Equals("RequestArrayOverride", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("RequestScalarArrayOverride", StringComparison.OrdinalIgnoreCase))).Returns(true);

            var loaded = _resolver.LoadProperties();

            Assert.AreEqual(typeof(string[]), loaded["BaseHosts"].Type);
            CollectionAssert.AreEqual(new[] { "host1.dv01.local", "host2.dv01.local" }, (string[])loaded["BaseHosts"].Value);

            Assert.AreEqual(typeof(string[]), loaded["ExpandedHosts"].Type);
            CollectionAssert.AreEqual(
                new[] { "host1.dv01.local", "host2.dv01.local", "host3.dv01.local" },
                (string[])loaded["ExpandedHosts"].Value);

            // Secure array elements are NOT interpolated or C#-evaluated
            CollectionAssert.AreEqual(new[] { "sec-$EnvTag$-1", "fn:1+1" }, (string[])loaded["SecureTokens"].Value);
            CollectionAssert.AreEqual(new[] { "raw-secret-element" }, (string[])loaded["SecureScalarFallback"].Value);

            // Non-JSON scalar fallback for IsArray=true still wraps into string[]
            CollectionAssert.AreEqual(new[] { "node-dv01" }, (string[])loaded["PlainScalarFallback"].Value);
            CollectionAssert.AreEqual(new[] { "host1.dv01.local", "host2.dv01.local" }, (string[])loaded["AliasedArray"].Value);
            CollectionAssert.AreEqual(new[] { "[not-valid-json" }, (string[])loaded["MalformedJsonArray"].Value);

            // Interpolating an array inside a composite scalar string joins with commas
            Assert.AreEqual("hosts=host1.dv01.local,host2.dv01.local!", loaded["JoinedSummary"].Value);

            // Pre-resolved string[] value stored via SetPropertyValue
            _resolver.SetPropertyValue("BaseHosts", new VariableValue { Value = new[] { "pre1", "pre2" }, Type = typeof(string[]) });
            CollectionAssert.AreEqual(new[] { "pre1", "pre2" }, (string[])_resolver.GetPropertyValue("BaseHosts")!.Value);

            // Request-supplied array skips C# expression evaluation
            _resolver.SetRequestSuppliedPropertyValue("RequestArrayOverride", "[\"fn:1+1\",\"$EnvTag$\"]");
            CollectionAssert.AreEqual(new[] { "fn:1+1", "dv01" }, (string[])_resolver.GetPropertyValue("RequestArrayOverride")!.Value);

            _resolver.SetRequestSuppliedPropertyValue("RequestScalarArrayOverride", "fn:1+1");
            CollectionAssert.AreEqual(new[] { "fn:1+1" }, (string[])_resolver.GetPropertyValue("RequestScalarArrayOverride")!.Value);

            // Repository-backed (not in localProperties) array lookup
            _propertyValuesPersistentSource.GetCachedPropertyValue("RepoOnlyArray")
                .Returns(new PropertyValueDto
                {
                    Value = "[\"repo-$EnvTag$\"]",
                    Property = new PropertyApiModel { Name = "RepoOnlyArray", Secure = false, IsArray = true }
                });
            var repoArray = _resolver.GetPropertyValue("RepoOnlyArray");
            Assert.IsNotNull(repoArray);
            CollectionAssert.AreEqual(new[] { "repo-dv01" }, (string[])repoArray.Value);
        }
    }
} 