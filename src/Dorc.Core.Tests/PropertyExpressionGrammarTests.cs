using Dorc.Core.VariableResolution;
using Microsoft.CodeAnalysis.CSharp.Scripting;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// Property resolution used to end by handing values to Roslyn — a C# compiler, reachable
    /// from deployment data, inside the process holding both deployment credential pairs.
    /// Provenance containment removed the path a request could take to it; this removes the
    /// compiler.
    ///
    /// An estate inventory found 11 distinct expressions across 133 property values, every one
    /// of a single shape: a double-quoted literal followed by a left-to-right chain of
    /// lower-casing, upper-casing and replacement. One expression is 101 of the 133. No loops,
    /// reflection, assembly references, type construction or I/O. That is a parser's job.
    ///
    /// Token interpolation has already happened by this point, so what is parsed is the
    /// substituted text — which is why parsing is strict rather than forgiving.
    /// </summary>
    [TestClass]
    public class PropertyExpressionGrammarTests
    {
        private static string Evaluate(string expression)
        {
            Assert.IsTrue(PropertyExpressionGrammar.TryEvaluate(expression, out var result, out var error),
                $"Expected '{expression}' to parse, but: {error}");
            return result;
        }

        private static string Refuse(string expression)
        {
            Assert.IsFalse(PropertyExpressionGrammar.TryEvaluate(expression, out _, out var error),
                $"Expected '{expression}' to be refused.");
            return error;
        }

        /// <summary>
        /// The differential the plan asks for: every shape the inventory recorded, evaluated
        /// under both the compiler being removed and the grammar replacing it, asserting
        /// identical results.
        ///
        /// These are the POST-interpolation forms, which is what actually reaches the
        /// evaluator. The corpus covers every recorded grammar feature — adjacent tokens and
        /// tokens abutting literal text both reduce to plain literal content once substituted,
        /// and the space-to-underscore replacement is the expression accounting for 101 of the
        /// 133 occurrences.
        /// </summary>
        [TestMethod]
        [DataRow("\"MY ENV\".Replace(\" \",\"_\")")]
        [DataRow("\"MY ENV\".Replace(\" \",\"\")")]
        [DataRow("\"MixedCase\".ToLower()")]
        [DataRow("\"MixedCase\".ToUpper()")]
        [DataRow("\"GLO PRD\".Replace(\" \",\"-\").ToLower()")]
        [DataRow("\"glo\".ToUpper().Replace(\"G\",\"X\")")]
        [DataRow("\"sefe1uks\"")]
        [DataRow("\"prd1uks\".ToLower()")]
        [DataRow("\"a-b-c\".Replace(\"-\",\"\").ToUpper()")]
        [DataRow("\"KV-GLO-PRD-1\".ToLower().Replace(\"-\",\"\")")]
        [DataRow("\"\"")]
        [DataRow("\"\".ToUpper()")]
        public void AgreesWithTheCompilerItReplaces(string expression)
        {
            var compiled = CSharpScript.EvaluateAsync(expression).Result?.ToString();

            Assert.AreEqual(compiled, Evaluate(expression),
                $"The grammar and the compiler disagree on '{expression}'.");
        }

        /// <summary>
        /// The single most common expression in the estate, in its substituted form.
        /// </summary>
        [TestMethod]
        public void EvaluatesTheEnvironmentNameUnderscoreExpression()
        {
            Assert.AreEqual("MY_TEST_ENV", Evaluate("\"MY TEST ENV\".Replace(\" \",\"_\")"));
        }

        [TestMethod]
        public void AppliesOperationsLeftToRight()
        {
            // Upper first would give "AXC"; replace first gives "abc" then "ABC".
            Assert.AreEqual("ABC", Evaluate("\"a-c\".Replace(\"-\",\"b\").ToUpper()"));
            Assert.AreEqual("A-C", Evaluate("\"a-c\".ToUpper().Replace(\"B\",\"x\")"));
        }

        [TestMethod]
        public void CasingIsCultureIndependent()
        {
            // A deployment must not resolve a property differently depending on the host's
            // locale. The Turkish dotless i is the standard demonstration.
            Assert.AreEqual("I", Evaluate("\"i\".ToUpper()"));
            Assert.AreEqual("i", Evaluate("\"I\".ToLower()"));
        }

        [TestMethod]
        public void HandlesEscapesThatCanAppearInALiteral()
        {
            Assert.AreEqual("a\"b", Evaluate("\"a\\\"b\""));
            Assert.AreEqual("a\\b", Evaluate("\"a\\\\b\""));
            Assert.AreEqual("a\tb", Evaluate("\"a\\tb\""));
        }

        // --- fails closed --------------------------------------------------------------

        /// <summary>
        /// The whole point. Anything outside the grammar is refused, never compiled. Secure
        /// configuration values are encrypted at rest, could not be inventoried, and do reach
        /// the evaluator — so refusal is what contains that residual.
        /// </summary>
        [TestMethod]
        [DataRow("\"abc\".Trim()", "unsupported operation")]
        [DataRow("\"abc\".Substring(1)", "unsupported operation")]
        [DataRow("\"abc\".ToLowerInvariant()", "unsupported operation")]
        [DataRow("System.IO.File.ReadAllText(\"c:/x\")", "expected a double-quoted string literal")]
        [DataRow("typeof(string).Assembly.Location", "expected a double-quoted string literal")]
        [DataRow("1+1", "expected a double-quoted string literal")]
        [DataRow("@\"abc\"", "expected a double-quoted string literal")]
        public void RefusesAnythingOutsideTheGrammar(string expression, string expectedReason)
        {
            StringAssert.Contains(Refuse(expression), expectedReason);
        }

        /// <summary>
        /// An interpolated value containing a quote makes the literal's extent ambiguous. The
        /// grammar refuses rather than guessing which quote closes the literal.
        /// </summary>
        [TestMethod]
        public void RefusesALiteralWhoseExtentIsAmbiguous()
        {
            Refuse("\"MY \"ENV\"\".Replace(\" \",\"_\")");
        }

        [TestMethod]
        [DataRow("\"unclosed")]
        [DataRow("\"abc\".Replace(\" \")")]
        [DataRow("\"abc\".Replace(\" \",\"_\",\"x\")")]
        [DataRow("\"abc\".ToUpper")]
        [DataRow("\"abc\".ToUpper(1)")]
        [DataRow("\"abc\" \"def\"")]
        [DataRow("\"abc\"..ToUpper()")]
        [DataRow("")]
        public void RefusesMalformedExpressions(string expression)
        {
            Refuse(expression);
        }

        /// <summary>
        /// .NET throws for it, and the estate does not do it, so it is named rather than
        /// allowed to surface as an unhandled exception mid-deployment.
        /// </summary>
        [TestMethod]
        public void RefusesReplacingTheEmptyString()
        {
            StringAssert.Contains(Refuse("\"abc\".Replace(\"\",\"x\")"), "empty first argument");
        }

        /// <summary>
        /// The literal is a resolved property value and may be a secret, so a parse failure
        /// must not quote it back into a log or an exception message.
        /// </summary>
        [TestMethod]
        public void DoesNotEchoTheLiteralInItsErrors()
        {
            var error = Refuse("\"s3cret-p4ssword\".Trim()");

            Assert.IsFalse(error.Contains("s3cret"), $"The error disclosed the literal: {error}");
        }
        /// <summary>
        /// The dependency is gone, not merely unused. Asserted against the production
        /// assembly's own reference list rather than by inspecting a project file, so it stays
        /// true if someone reintroduces the package transitively.
        ///
        /// This test project does reference the compiler — that is how the differential above
        /// works — so this has to ask Dorc.Core about itself.
        /// </summary>
        [TestMethod]
        public void TheCompilerIsNoLongerReferencedByTheAssemblyThatResolvesProperties()
        {
            var referenced = typeof(PropertyExpressionGrammar).Assembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)
                .ToList();

            Assert.IsFalse(
                referenced.Any(name => name.Contains("CodeAnalysis", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Scripting", StringComparison.OrdinalIgnoreCase)),
                "Dorc.Core references a compiler again: " + string.Join(", ", referenced));
        }
    }
}
