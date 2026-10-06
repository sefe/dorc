using Dorc.Core;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class ApiCallerErrorMessageTests
    {
        [TestMethod]
        public void ExtractErrorMessage_BareJsonString_IsUnwrapped()
        {
            var result = ApiCaller.ExtractErrorMessage("\"Project name must be provided.\"");

            Assert.AreEqual("Project name must be provided.", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonStringWithEscapes_IsDecoded()
        {
            var result = ApiCaller.ExtractErrorMessage("\"line1\\nline2\"");

            Assert.AreEqual("line1\nline2", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonObjectWithMessage_ReturnsMessage()
        {
            var result = ApiCaller.ExtractErrorMessage("{\"RequestIds\":[],\"Success\":false,\"Message\":\"Environment not found\"}");

            Assert.AreEqual("Environment not found", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonObjectWithLowercaseMessage_ReturnsMessage()
        {
            var result = ApiCaller.ExtractErrorMessage("{\"message\":\"boom\"}");

            Assert.AreEqual("boom", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_ProblemDetailsWithDetail_ReturnsDetail()
        {
            var result = ApiCaller.ExtractErrorMessage("{\"title\":\"Bad Request\",\"status\":400,\"detail\":\"The build was not found\"}");

            Assert.AreEqual("The build was not found", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_ProblemDetailsWithOnlyTitle_ReturnsTitle()
        {
            var result = ApiCaller.ExtractErrorMessage("{\"title\":\"One or more validation errors occurred.\",\"status\":400}");

            Assert.AreEqual("One or more validation errors occurred.", result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonObjectWithEmptyMessage_FallsBackToRawContent()
        {
            const string raw = "{\"Message\":\"\"}";

            var result = ApiCaller.ExtractErrorMessage(raw);

            Assert.AreEqual(raw, result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonObjectWithoutRecognisedProperty_FallsBackToRawContent()
        {
            const string raw = "{\"errors\":{\"BuildText\":[\"required\"]}}";

            var result = ApiCaller.ExtractErrorMessage(raw);

            Assert.AreEqual(raw, result);
        }

        [TestMethod]
        public void ExtractErrorMessage_PlainText_IsReturnedAsIs()
        {
            const string raw = "Internal server error";

            var result = ApiCaller.ExtractErrorMessage(raw);

            Assert.AreEqual(raw, result);
        }

        [TestMethod]
        public void ExtractErrorMessage_JsonNumber_FallsBackToRawContent()
        {
            var result = ApiCaller.ExtractErrorMessage("42");

            Assert.AreEqual("42", result);
        }
    }
}
