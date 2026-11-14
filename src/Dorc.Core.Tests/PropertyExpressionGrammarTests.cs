using System.Globalization;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

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
            try
            {
                return PropertyExpressionGrammar.Evaluate(expression);
            }
            catch (PropertyExpressionParseException refusal)
            {
                Assert.Fail($"Expected '{expression}' to parse, but: {refusal.Message}");
                throw;
            }
        }

        private static string Refuse(string expression)
        {
            return Assert.ThrowsExactly<PropertyExpressionParseException>(
                () => PropertyExpressionGrammar.Evaluate(expression),
                $"Expected '{expression}' to be refused.").Message;
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
            var priorCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

                var compiled = CSharpScript.EvaluateAsync(expression).Result?.ToString();

                Assert.AreEqual(compiled, Evaluate(expression),
                    $"The grammar and the compiler disagree on '{expression}'.");
            }
            finally
            {
                CultureInfo.CurrentCulture = priorCulture;
            }
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
            var priorCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

                Assert.AreEqual("I", Evaluate("\"i\".ToUpper()"));
                Assert.AreEqual("i", Evaluate("\"I\".ToLower()"));
            }
            finally
            {
                CultureInfo.CurrentCulture = priorCulture;
            }
        }

        [TestMethod]
        public void UsesInvariantCasingUnderTurkishCurrentCulture()
        {
            var priorCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

                Assert.AreEqual("MIXEDCASE", Evaluate("\"MixedCase\".ToUpper()"));
                Assert.AreEqual("mixedcase", Evaluate("\"MixedCase\".ToLower()"));
                Assert.AreEqual("mixedcase", Evaluate("\"MIXEDCASE\".ToLower()"));

                var evaluator = new PropertyExpressionEvaluator(NullLogger<PropertyExpressionEvaluator>.Instance);
                Assert.AreEqual("MIXEDCASE", evaluator.Evaluate("fn:\"MixedCase\".ToUpper()"));
                Assert.AreEqual("mixedcase", evaluator.Evaluate("fn:\"MIXEDCASE\".ToLower()"));
            }
            finally
            {
                CultureInfo.CurrentCulture = priorCulture;
            }
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

        [TestMethod]
        public void LoadProperties_InvalidExpressionThrowsAndLogsPropertyKeyWithoutValue()
        {
            const string offendingKey = "BrokenSecretExpression";
            const string secretLiteral = "s3cret-v4lue";

            var resolverLogs = new List<(LogLevel Level, string Message, Exception? Exception)>();
            var evaluatorLogs = new List<string>();

            var resolverLogger = Substitute.For<ILogger<VariableResolver>>();
            resolverLogger.WhenForAnyArgs(x => x.Log<object>(default, default, default!, default, default!))
                .Do(call => resolverLogs.Add((
                    (LogLevel)call.Args()[0]!,
                    call.Args()[2]?.ToString() ?? string.Empty,
                    call.Args()[3] as Exception)));

            var evaluatorLogger = Substitute.For<ILogger<PropertyExpressionEvaluator>>();
            evaluatorLogger.WhenForAnyArgs(x => x.Log<object>(default, default, default!, default, default!))
                .Do(call => evaluatorLogs.Add(call.Args()[2]?.ToString() ?? string.Empty));

            var loggerFactory = Substitute.For<ILoggerFactory>();
            loggerFactory.CreateLogger<VariableResolver>().Returns(resolverLogger);
            loggerFactory.CreateLogger<PropertyExpressionEvaluator>().Returns(evaluatorLogger);

            var persistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            persistentSource.LoadAllPropertiesIntoCache().Returns(
                new Dictionary<string, PropertyValueDto>
                {
                    ["ValidSlug"] = new() { Value = "fn:\"MY ENV\".Replace(\" \",\"_\")" },
                    [offendingKey] = new() { Value = $"fn:\"{secretLiteral}\".Trim()" }
                });
            persistentSource.IsCachedPropertySecure(Arg.Any<string>()).Returns(false);

            var resolver = new VariableResolver(persistentSource, loggerFactory, new PropertyEvaluator());

            var error = Assert.ThrowsExactly<PropertyExpressionEvaluationException>(
                () => resolver.LoadProperties());

            Assert.IsInstanceOfType<PropertyExpressionParseException>(error.InnerException);
            Assert.IsFalse(error.Message.Contains(secretLiteral, StringComparison.Ordinal));
            Assert.IsTrue(
                resolverLogs.Any(entry =>
                    entry.Level == LogLevel.Error
                    && entry.Message.Contains(offendingKey, StringComparison.Ordinal)),
                "Expected VariableResolver to log the offending property key.");
            Assert.IsFalse(
                resolverLogs.Any(entry =>
                    entry.Message.Contains(secretLiteral, StringComparison.Ordinal)
                    || (entry.Exception?.ToString().Contains(secretLiteral, StringComparison.Ordinal) ?? false)),
                "VariableResolver log disclosed the property value.");
            Assert.IsFalse(
                evaluatorLogs.Any(message => message.Contains(secretLiteral, StringComparison.Ordinal)),
                "PropertyExpressionEvaluator log disclosed the property value.");
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

        [TestMethod]
        public void DorcCoreDependencyGraphDoesNotReferenceCSharpScripting()
        {
            var depsJsonPath = Path.Join(AppContext.BaseDirectory, "Dorc.Core.Tests.deps.json");
            Assert.IsTrue(File.Exists(depsJsonPath), $"Expected '{depsJsonPath}' to exist.");

            using var document = JsonDocument.Parse(File.ReadAllText(depsJsonPath));
            var targetFrameworkGraph = document.RootElement
                .GetProperty("targets")
                .EnumerateObject()
                .Single()
                .Value;

            var dorcCoreNodeName = targetFrameworkGraph
                .EnumerateObject()
                .Select(entry => entry.Name)
                .Single(name => name.StartsWith("Dorc.Core/", StringComparison.Ordinal));

            var reachablePackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>();
            pending.Enqueue(dorcCoreNodeName);

            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                if (!reachablePackages.Add(current))
                {
                    continue;
                }

                if (!targetFrameworkGraph.TryGetProperty(current, out var node)
                    || !node.TryGetProperty("dependencies", out var dependencies))
                {
                    continue;
                }

                foreach (var dependency in dependencies.EnumerateObject())
                {
                    var matchingTargets = targetFrameworkGraph
                        .EnumerateObject()
                        .Select(entry => entry.Name)
                        .Where(name => name.StartsWith(dependency.Name + "/", StringComparison.OrdinalIgnoreCase));

                    foreach (var match in matchingTargets)
                    {
                        pending.Enqueue(match);
                    }
                }
            }

            Assert.IsFalse(
                reachablePackages.Any(pkg => pkg.Contains("Scripting", StringComparison.OrdinalIgnoreCase)),
                "Dorc.Core transitive dependency graph still references C# scripting: "
                + string.Join(", ", reachablePackages.Where(pkg => pkg.Contains("Scripting", StringComparison.OrdinalIgnoreCase))));
        }
    }
}
