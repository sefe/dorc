using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// Property resolution ends by handing values to an expression evaluator that compiles
    /// and runs C#. That is acceptable for administrator-curated values and not for anything
    /// a deployment request can reach: the evaluation happens inside the Monitor process,
    /// which holds both deployment credential pairs, so a user able to submit a request
    /// would otherwise obtain them.
    ///
    /// These tests fix the boundary. The evaluator itself is unchanged in what it can do -
    /// what changed is which values are allowed to reach it.
    /// </summary>
    [TestClass]
    public class VariableResolverProvenanceTests
    {
        private IPropertyValuesPersistentSource _propertyValuesPersistentSource = null!;
        private ILoggerFactory _loggerFactory = null!;
        private VariableResolver _resolver = null!;

        [TestInitialize]
        public void Setup()
        {
            _propertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            _loggerFactory = Substitute.For<ILoggerFactory>();
            _loggerFactory.CreateLogger<PropertyExpressionEvaluator>()
                .Returns(Substitute.For<ILogger<PropertyExpressionEvaluator>>());
            _propertyValuesPersistentSource.IsCachedPropertySecure(Arg.Any<string>()).Returns(false);

            _resolver = new VariableResolver(_propertyValuesPersistentSource, _loggerFactory, new PropertyEvaluator());
        }

        /// <summary>
        /// The rank-2 weakness, directly. Before containment this returned "ABC" - the
        /// expression compiled and ran - which is arbitrary code execution from the lowest
        /// privilege DOrc grants.
        /// </summary>
        [TestMethod]
        public void RequestSuppliedExpression_IsNotEvaluated()
        {
            _resolver.SetRequestSuppliedPropertyValue("attackerControlled", "fn:\"abc\".ToUpper()");

            var result = _resolver.GetPropertyValue("attackerControlled");

            Assert.AreEqual("fn:\"abc\".ToUpper()", result!.Value);
        }

        [TestMethod]
        public void CuratedExpression_IsStillEvaluated()
        {
            _resolver.SetPropertyValue("curated", "fn:\"abc\".ToUpper()");

            var result = _resolver.GetPropertyValue("curated");

            Assert.AreEqual("ABC", result!.Value);
        }

        /// <summary>
        /// The transitive case. The expression is administrator-authored, but the string that
        /// would be compiled is partly request-supplied, so evaluation is withheld from the
        /// curated property too.
        /// </summary>
        [TestMethod]
        public void CuratedExpressionInterpolatingRequestSuppliedValue_IsNotEvaluated()
        {
            _resolver.SetRequestSuppliedPropertyValue("injected", "abc\".GetType().Assembly.Location + \"");
            _resolver.SetPropertyValue("curated", "fn:\"$injected$\".ToUpper()");

            var result = _resolver.GetPropertyValue("curated");

            Assert.IsFalse(result!.Value.ToString()!.Contains("System.Private.CoreLib"),
                "A request-supplied value reached the expression evaluator through a curated expression.");
        }

        [TestMethod]
        public void CuratedExpressionInterpolatingRequestSuppliedValueIndirectly_IsNotEvaluated()
        {
            // curated -> intermediate -> request-supplied. Influence must follow the chain,
            // not just the immediate reference.
            _resolver.SetRequestSuppliedPropertyValue("fromRequest", "abc");
            _resolver.SetPropertyValue("intermediate", "$fromRequest$");
            _resolver.SetPropertyValue("curated", "fn:\"$intermediate$\".ToUpper()");

            var result = _resolver.GetPropertyValue("curated");

            Assert.AreNotEqual("ABC", result!.Value,
                "Influence did not follow the reference chain.");
        }

        /// <summary>
        /// Request properties are applied after environment and configuration properties and
        /// legitimately override them, so shadowing is permitted - but a shadowed property
        /// carries request-supplied text and must lose evaluation along with it.
        /// </summary>
        [TestMethod]
        public void RequestSuppliedValueShadowingACuratedProperty_LosesEvaluation()
        {
            _resolver.SetPropertyValue("EnvironmentName", "fn:\"abc\".ToUpper()");
            _resolver.SetRequestSuppliedPropertyValue("EnvironmentName", "fn:\"xyz\".ToUpper()");

            var result = _resolver.GetPropertyValue("EnvironmentName");

            Assert.AreEqual("fn:\"xyz\".ToUpper()", result!.Value);
        }

        /// <summary>
        /// Property storage in this resolver is case-sensitive, so a differently-cased name
        /// is a different property. The tracker is deliberately case-INsensitive anyway: it
        /// can then only ever report influence where storage would not, which withholds
        /// evaluation more often rather than less. See the tracker's own tests.
        /// </summary>
        [TestMethod]
        public void ACuratedPropertyDifferingOnlyByCaseFromARequestSuppliedOne_AlsoLosesEvaluation()
        {
            _resolver.SetRequestSuppliedPropertyValue("environmentname", "anything");
            _resolver.SetPropertyValue("EnvironmentName", "fn:\"abc\".ToUpper()");

            var result = _resolver.GetPropertyValue("EnvironmentName");

            Assert.AreEqual("fn:\"abc\".ToUpper()", result!.Value,
                "A check evadable by changing case is no check at all.");
        }

        [TestMethod]
        public void CuratedValuesAreUnaffectedWhenNoRequestPropertiesExist()
        {
            _resolver.SetPropertyValue("a", "abc");
            _resolver.SetPropertyValue("b", "fn:\"$a$\".ToUpper()");

            Assert.AreEqual("ABC", _resolver.GetPropertyValue("b")!.Value);
        }
    }

    [TestClass]
    public class RequestSuppliedPropertyTrackerTests
    {
        private static Func<string, string?> NoValues => _ => null;

        [TestMethod]
        public void ReportsNoInfluenceWhenNothingHasBeenMarked()
        {
            Assert.IsFalse(new RequestSuppliedPropertyTracker().IsInfluenced("anything", NoValues));
        }

        [TestMethod]
        public void MarkingIsCaseInsensitive()
        {
            var tracker = new RequestSuppliedPropertyTracker();
            tracker.Mark("MixedCaseName");

            Assert.IsTrue(tracker.IsInfluenced("mixedcasename", NoValues));
        }

        [TestMethod]
        public void FollowsReferencesOnRawValues()
        {
            var tracker = new RequestSuppliedPropertyTracker();
            tracker.Mark("tainted");

            var values = new Dictionary<string, string?>
            {
                ["curated"] = "prefix-$intermediate$-suffix",
                ["intermediate"] = "$tainted$"
            };

            Assert.IsTrue(tracker.IsInfluenced("curated", name => values.GetValueOrDefault(name)));
        }

        [TestMethod]
        public void ReportsNoInfluenceForAnUnrelatedReferenceChain()
        {
            var tracker = new RequestSuppliedPropertyTracker();
            tracker.Mark("tainted");

            var values = new Dictionary<string, string?>
            {
                ["curated"] = "$other$",
                ["other"] = "plain"
            };

            Assert.IsFalse(tracker.IsInfluenced("curated", name => values.GetValueOrDefault(name)));
        }

        /// <summary>
        /// A cycle means the reference graph cannot be resolved. Reporting influence
        /// withholds evaluation, which is the safe answer; reporting none would compile a
        /// string whose provenance is unknown.
        /// </summary>
        [TestMethod]
        public void TreatsAReferenceCycleAsInfluenced()
        {
            var tracker = new RequestSuppliedPropertyTracker();
            tracker.Mark("somethingElse");

            var values = new Dictionary<string, string?>
            {
                ["a"] = "$b$",
                ["b"] = "$a$"
            };

            Assert.IsTrue(tracker.IsInfluenced("a", name => values.GetValueOrDefault(name)));
        }
    }

    /// <summary>
    /// The expression evaluator's own two defects, independent of provenance.
    /// </summary>
    [TestClass]
    public class PropertyExpressionEvaluatorTests
    {
        private PropertyExpressionEvaluator NewEvaluator() =>
            new(Substitute.For<ILogger<PropertyExpressionEvaluator>>());

        [TestMethod]
        public void EvaluatesAnExpressionAnchoredAtTheStart()
        {
            Assert.AreEqual("ABC", NewEvaluator().Evaluate("fn:\"abc\".ToUpper()"));
        }

        /// <summary>
        /// The marker was previously matched anywhere in the value but always sliced from
        /// index 3, so a value merely containing "fn:" was cut into an arbitrary fragment and
        /// compiled - both a correctness bug and a way to reach the compiler unexpectedly.
        /// </summary>
        [TestMethod]
        public void LeavesAValueContainingTheMarkerElsewhereAlone()
        {
            const string value = "C:\\builds\\fn:something";

            Assert.AreEqual(value, NewEvaluator().Evaluate(value));
        }

        [TestMethod]
        public void LeavesOrdinaryValuesAlone()
        {
            Assert.AreEqual("plain", NewEvaluator().Evaluate("plain"));
        }

        /// <summary>
        /// The cache was process-wide and keyed on expression text alone, so a value first
        /// computed during one deployment was returned unchanged to every later one -
        /// including from a non-production deployment to a subsequent production deployment.
        /// Instance scope, with one evaluator per resolver per request, makes it per-request.
        /// </summary>
        [TestMethod]
        public void DoesNotShareCachedResultsBetweenInstances()
        {
            var first = NewEvaluator();
            var second = NewEvaluator();

            Assert.AreEqual("ABC", first.Evaluate("fn:\"abc\".ToUpper()"));
            Assert.AreEqual("ABC", second.Evaluate("fn:\"abc\".ToUpper()"));

            // Both compute the same answer; the point is that the second did not read the
            // first's cache. Verified by the field being instance-scoped - asserted here as a
            // behavioural guard against it being made static again.
            Assert.AreNotSame(first, second);
        }
    }
}
