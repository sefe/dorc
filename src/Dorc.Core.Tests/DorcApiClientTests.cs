using Dorc.Core;
using Microsoft.Extensions.Configuration;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class DorcApiClientTests
    {
        [TestMethod]
        public void ExtractErrorMessage_BareJsonString_IsUnwrapped()
        {
            var result = DorcApiClient.ExtractErrorMessage("\"Project name must be provided.\"");

            Assert.AreEqual("Project name must be provided.", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonStringWithEscapes_IsDecoded()
        {
            var result = DorcApiClient.ExtractErrorMessage("\"line1\\nline2\"");

            Assert.AreEqual("line1\nline2", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonObjectWithMessage_ReturnsMessage()
        {
            var result = DorcApiClient.ExtractErrorMessage("{\"RequestIds\":[],\"Success\":false,\"Message\":\"Environment not found\"}");

            Assert.AreEqual("Environment not found", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonObjectWithLowercaseMessage_ReturnsMessage()
        {
            var result = DorcApiClient.ExtractErrorMessage("{\"message\":\"boom\"}");

            Assert.AreEqual("boom", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_ProblemDetailsWithDetail_ReturnsDetail()
        {
            var result = DorcApiClient.ExtractErrorMessage("{\"title\":\"Bad Request\",\"status\":400,\"detail\":\"The build was not found\"}");

            Assert.AreEqual("The build was not found", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_ProblemDetailsWithOnlyTitle_ReturnsTitle()
        {
            var result = DorcApiClient.ExtractErrorMessage("{\"title\":\"One or more validation errors occurred.\",\"status\":400}");

            Assert.AreEqual("One or more validation errors occurred.", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonObjectWithEmptyMessage_FallsBackToRawContent()
        {
            const string raw = "{\"Message\":\"\"}";

            var result = DorcApiClient.ExtractErrorMessage(raw);

            Assert.AreEqual(raw, result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonObjectWithoutRecognisedProperty_FallsBackToRawContent()
        {
            const string raw = "{\"errors\":{\"BuildText\":[\"required\"]}}";

            var result = DorcApiClient.ExtractErrorMessage(raw);

            Assert.AreEqual(raw, result);
        }

        [TestMethod]
        public void ExtractErrorMessage_PlainText_IsReturnedAsIs()
        {
            const string raw = "Internal server error";

            var result = DorcApiClient.ExtractErrorMessage(raw);

            Assert.AreEqual(raw, result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonNumber_FallsBackToRawContent()
        {
            var result = DorcApiClient.ExtractErrorMessage("42");

            Assert.AreEqual("42", result);
        }
    }
}
