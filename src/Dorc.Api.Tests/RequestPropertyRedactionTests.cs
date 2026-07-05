using Dorc.ApiModel;
using Dorc.Core;

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

        // ---------- RedactRequestDetailsXml ----------
        // The XML payloads below are produced by the real
        // Dorc.Core.DeploymentRequestDetailSerializer so these tests also
        // prove the serializer round-trip of the PropertyPair.IsSensitive
        // flag, not just string surgery on a hand-written fixture.

        private static string SerializeDetail(params PropertyPair[] properties)
        {
            var detail = new DeploymentRequestDetail
            {
                EnvironmentName = "TEVO DV 11",
                Components = new List<string> { "storage-account" },
                Properties = properties.ToList(),
            };
            return new DeploymentRequestDetailSerializer().Serialize(detail);
        }

        [TestMethod]
        public void RedactRequestDetailsXml_ReplacesSensitiveValue_PreservesNonSensitive()
        {
            var xml = SerializeDetail(
                new PropertyPair("rg", "rg-prod"),
                new PropertyPair("password", "hunter2-original") { IsSensitive = true });

            var redacted = RequestPropertyRedaction.RedactRequestDetailsXml(xml);

            Assert.IsFalse(redacted.Contains("hunter2-original"),
                "Sensitive value must not survive redaction anywhere in the payload.");
            StringAssert.Contains(redacted, RequestPropertyRedaction.Marker,
                "Sensitive value is replaced with the marker.");
            StringAssert.Contains(redacted, "rg-prod",
                "Non-sensitive value is preserved verbatim.");
        }

        [TestMethod]
        public void RedactRequestDetailsXml_NoSensitiveProperties_ReturnsSameInstance()
        {
            var xml = SerializeDetail(
                new PropertyPair("rg", "rg-prod"),
                new PropertyPair("loc", "uksouth"));

            var result = RequestPropertyRedaction.RedactRequestDetailsXml(xml);

            Assert.AreSame(xml, result,
                "Payloads without a sensitive property skip parsing and are returned unchanged (same instance).");
        }

        [TestMethod]
        public void RedactRequestDetailsXml_NullOrEmpty_ReturnedUnchanged()
        {
            Assert.IsNull(RequestPropertyRedaction.RedactRequestDetailsXml(null!));
            Assert.AreEqual(string.Empty, RequestPropertyRedaction.RedactRequestDetailsXml(string.Empty));
        }

        [TestMethod]
        public void RedactRequestDetailsXml_MalformedXml_ReturnedUnchanged()
        {
            // Contains the sensitive marker substring (so the cheap pre-check
            // does not short-circuit) but is not well-formed XML.
            const string malformed = "<DeploymentRequestDetail><Properties><PropertyPair>"
                + "<Name>password</Name><Value>hunter2-original</Value>"
                + "<IsSensitive>true</IsSensitive>";

            var result = RequestPropertyRedaction.RedactRequestDetailsXml(malformed);

            Assert.AreSame(malformed, result,
                "Malformed XML must be returned unchanged rather than throwing at a read surface.");
        }

        [TestMethod]
        public void RedactRequestDetailsXml_ResultStillDeserializes_WithMarkerValue()
        {
            var xml = SerializeDetail(
                new PropertyPair("rg", "rg-prod"),
                new PropertyPair("password", "hunter2-original") { IsSensitive = true });

            var redacted = RequestPropertyRedaction.RedactRequestDetailsXml(xml);
            var roundTripped = new DeploymentRequestDetailSerializer().Deserialize(redacted);

            var sensitive = roundTripped.Properties.Single(p => p.Name == "password");
            Assert.AreEqual(RequestPropertyRedaction.Marker, sensitive.Value,
                "Redacted payload must still deserialize, with the sensitive value replaced by the marker.");
            Assert.IsTrue(sensitive.IsSensitive,
                "IsSensitive flag survives redaction and round-trip.");

            var plain = roundTripped.Properties.Single(p => p.Name == "rg");
            Assert.AreEqual("rg-prod", plain.Value, "Non-sensitive value survives round-trip unchanged.");
            Assert.IsFalse(plain.IsSensitive);
        }
    }
}
