using Dorc.Core.VariableResolution;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class SafeExpressionValidatorTests
    {
        // ---- Rejected: arbitrary-code-execution / reflection vectors ----
        [DataTestMethod]
        [DataRow("System.IO.File.ReadAllText(\"/etc/passwd\")")]
        [DataRow("File.ReadAllText(\"x\")")]
        [DataRow("System.Diagnostics.Process.Start(\"calc\")")]
        [DataRow("Environment.Exit(1)")]
        [DataRow("AppDomain.CurrentDomain")]
        [DataRow("typeof(string)")]
        [DataRow("new System.Net.WebClient()")]
        [DataRow("\"x\".GetType()")]
        [DataRow("\"x\".GetType().Assembly")]
        [DataRow("Directory.GetFiles(\"c:/\")")]
        [DataRow("1 + 1; System.IO.File.ReadAllText(\"x\")")]  // trailing statement
        [DataRow("a => a")]                                       // lambda
        [DataRow("someVariable")]                                 // bare identifier
        // Unicode-escaped GetType: raw text is "GetType" but the compiler binds
        // it to GetType(). Must be caught via ValueText, not Text (regression for the
        // round-2 CRITICAL bypass that achieved arbitrary file write).
        [DataRow("\"\".GetType()")]
        [DataRow("\"\".Get\\u0054ype()")]  // literal T escape reaching the validator
        [DataRow("\"\".GetType().Assembly")]
        [DataRow("\"\".GetType().Assembly.CreateInstance(\"System.Text.StringBuilder\")")]
        [DataRow("\"\".GetType().Assembly.GetType(\"System.IO.File\").GetMethod(\"WriteAllText\")")]
        // Size-amplification DoS: PadLeft/PadRight are not on the instance allow-list.
        [DataRow("\"x\".PadRight(2000000000)")]
        [DataRow("\"x\".PadLeft(2000000000)")]
        // Predefined-type static receivers are not (currently) allow-listed.
        [DataRow("string.Format(\"{0}\", \"x\")")]
        [DataRow("int.Parse(\"5\")")]
        // Bare-identifier static receivers (Math/Convert) are rejected: the scripting
        // host has no imports so they cannot execute, and allowing them would rest the
        // safety guarantee on the runtime happening to lack a System import.
        [DataRow("Math.Max(1, 2)")]
        [DataRow("Convert.ToInt32(\"5\")")]
        public void IsSafe_RejectsDangerousExpressions(string expression)
        {
            Assert.IsFalse(SafeExpressionValidator.IsSafe(expression, out _), $"Should have rejected: {expression}");
        }

        // ---- Allowed: the simple string/math operations fn: is used for ----
        [DataTestMethod]
        [DataRow("\"abc\".ToUpper()")]
        [DataRow("\"ABC\".ToLower()")]
        [DataRow("2 + 3 * 4")]
        [DataRow("(1 + 2) * 3")]
        [DataRow("\"a\" + \"b\" + \"c\"")]
        [DataRow("\"hello world\".Substring(0, 5)")]
        [DataRow("\"  x  \".Trim()")]
        [DataRow("\"a-b-c\".Replace(\"-\", \"_\")")]
        [DataRow("\"abc\".Length")]
        [DataRow("\"abc\".Contains(\"b\")")]
        [DataRow("1 > 0 ? \"yes\" : \"no\"")]
        [DataRow("\"abc\".Substring(1).ToUpper()")]
        [DataRow("\"a\".Length + 2 * 3")]
        public void IsSafe_AllowsSimpleStringAndMathExpressions(string expression)
        {
            Assert.IsTrue(SafeExpressionValidator.IsSafe(expression, out var reason), $"Should have allowed '{expression}' but: {reason}");
        }

        [TestMethod]
        public void IsSafe_RejectsMalformedExpression()
        {
            Assert.IsFalse(SafeExpressionValidator.IsSafe("bad(", out _));
            Assert.IsFalse(SafeExpressionValidator.IsSafe("", out _));
            Assert.IsFalse(SafeExpressionValidator.IsSafe(null, out _));
        }
    }
}
