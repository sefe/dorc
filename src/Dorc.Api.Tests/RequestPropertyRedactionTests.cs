using Dorc.ApiModel;

namespace Dorc.Api.Tests
{
    /// <summary>
    /// Unit tests for the central RequestPropertyRedaction helper.
    /// The helper is the mechanism every redaction site uses; these tests
    /// pin its contract so any future site that consumes it inherits the
    /// guarantees verified here.
    /// </summary>
    [TestClass]
    public class RequestPropertyRedactionTests
    {
        [TestMethod]
        public void RedactCollection_ReturnsNullForNullSource_AsEmptyEnumerable()
        {
            var result = RequestPropertyRedaction.RedactCollection(null).ToList();
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void RedactCollection_LeavesNonSensitiveValuesUnchanged()
        {
            var input = new[]
            {
                new RequestProperty { PropertyName = "rg", PropertyValue = "rg-prod", IsSensitive = false },
                new RequestProperty { PropertyName = "loc", PropertyValue = "uksouth", IsSensitive = false },
            };

            var result = RequestPropertyRedaction.RedactCollection(input).ToList();

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("rg-prod", result[0].PropertyValue);
            Assert.AreEqual("uksouth", result[1].PropertyValue);
        }

        [TestMethod]
        public void RedactCollection_ReplacesSensitiveValuesWithMarker()
        {
            var input = new[]
            {
                new RequestProperty { PropertyName = "rg", PropertyValue = "rg-prod", IsSensitive = false },
                new RequestProperty { PropertyName = "password", PropertyValue = "hunter2-original", IsSensitive = true },
            };

            var result = RequestPropertyRedaction.RedactCollection(input).ToList();

            Assert.AreEqual("rg-prod", result[0].PropertyValue, "Non-sensitive value preserved.");
            Assert.AreEqual(RequestPropertyRedaction.Marker, result[1].PropertyValue,
                "Sensitive value replaced with marker.");
            Assert.AreEqual("password", result[1].PropertyName,
                "Sensitive entry's name is preserved.");
            Assert.IsTrue(result[1].IsSensitive,
                "Sensitive flag is preserved (so downstream consumers still know).");
        }

        [TestMethod]
        public void RedactCollection_DoesNotMutateOriginalSensitiveEntry()
        {
            var sensitive = new RequestProperty { PropertyName = "password", PropertyValue = "hunter2-original", IsSensitive = true };
            var input = new[] { sensitive };

            _ = RequestPropertyRedaction.RedactCollection(input).ToList();

            Assert.AreEqual("hunter2-original", sensitive.PropertyValue,
                "Helper must return new instances for sensitive entries; original input is untouched.");
        }

        [TestMethod]
        public void FormatForLog_EmptyOrNull_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, RequestPropertyRedaction.FormatForLog(null));
            Assert.AreEqual(string.Empty, RequestPropertyRedaction.FormatForLog(Array.Empty<RequestProperty>()));
        }

        [TestMethod]
        public void FormatForLog_RedactsSensitiveValuesInRenderedString()
        {
            var input = new[]
            {
                new RequestProperty { PropertyName = "rg", PropertyValue = "rg-prod", IsSensitive = false },
                new RequestProperty { PropertyName = "password", PropertyValue = "hunter2-original", IsSensitive = true },
            };

            var rendered = RequestPropertyRedaction.FormatForLog(input);

            StringAssert.Contains(rendered, "rg=rg-prod", "Non-sensitive entry rendered verbatim.");
            StringAssert.Contains(rendered, $"password={RequestPropertyRedaction.Marker}",
                "Sensitive entry's value is replaced with the marker in the rendered string.");
            Assert.IsFalse(rendered.Contains("hunter2-original"),
                "Original sensitive value must not appear anywhere in the rendered string.");
        }

        [TestMethod]
        public void FormatForLog_HandlesNullPropertyValue()
        {
            var input = new[]
            {
                new RequestProperty { PropertyName = "k", PropertyValue = null!, IsSensitive = false },
            };

            var rendered = RequestPropertyRedaction.FormatForLog(input);

            Assert.AreEqual("k=", rendered);
        }

        [TestMethod]
        public void IsSensitive_DefaultsToFalse_OnNewlyConstructedRequestProperty()
        {
            var p = new RequestProperty { PropertyName = "x", PropertyValue = "y" };
            Assert.IsFalse(p.IsSensitive,
                "Existing JSON payloads omitting IsSensitive must deserialise to false (additive change).");
        }
    }
}
