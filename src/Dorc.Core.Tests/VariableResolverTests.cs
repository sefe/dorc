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
            var secureValue = new VariableValue { Value = "secret", Type = typeof(string) };
            _resolver.SetPropertyValue(propertyName, secureValue);
            _propertyValuesPersistentSource.IsCachedPropertySecure(propertyName).Returns(true);
            var result = _resolver.GetPropertyValue(propertyName);
            Assert.IsNotNull(result);
            Assert.AreEqual(secureValue.Value, result.Value);
        }

        [TestMethod]
        public void GetPropertyValue_InvalidExpressionFailsResolution()
        {
            const string property = "invalidExpression";
            _resolver.SetPropertyValue(
                property,
                new VariableValue { Value = "fn:bad(", Type = typeof(string) });
            _propertyValuesPersistentSource.IsCachedPropertySecure(property).Returns(false);

            Assert.ThrowsExactly<PropertyExpressionEvaluationException>(
                () => _resolver.GetPropertyValue(property));
        }
    }
} 