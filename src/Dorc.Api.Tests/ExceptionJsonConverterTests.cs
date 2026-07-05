using Dorc.Api.Services;
using System.Text.Json;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class ExceptionJsonConverterTests
    {
        private static string Serialize(Exception ex)
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new ExceptionJsonConverter());
            return JsonSerializer.Serialize(ex, options);
        }

        [TestMethod]
        public void Write_DoesNotLeakStackTraceOrInnerException()
        {
            Exception caught;
            try
            {
                try
                {
                    throw new InvalidOperationException("inner secret detail at C:\\secret\\path");
                }
                catch (Exception inner)
                {
                    throw new ApplicationException("outer failure", inner);
                }
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            var json = Serialize(caught);

            StringAssert.DoesNotMatch(json, new System.Text.RegularExpressions.Regex("StackTrace"));
            StringAssert.DoesNotMatch(json, new System.Text.RegularExpressions.Regex("InnerException"));
            // The inner exception's message (which held a sensitive path) must not leak.
            StringAssert.DoesNotMatch(json, new System.Text.RegularExpressions.Regex("secret"));
            // A generic message is present.
            StringAssert.Contains(json, "An unexpected error occurred");
        }

        [TestMethod]
        public void Write_EmitsShortTypeNameOnly_NotFullyQualified()
        {
            var json = Serialize(new ApplicationException("boom"));

            StringAssert.Contains(json, "ApplicationException");
            StringAssert.DoesNotMatch(json, new System.Text.RegularExpressions.Regex("System\\.ApplicationException"));
        }
    }
}
