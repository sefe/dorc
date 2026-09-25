using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;
using System.Security.Principal;
using System.Text;

namespace Dorc.Api.Tests.Sources
{
    /// <summary>
    /// The recorded content hash is what a script is permitted to be. Three properties follow
    /// from that and are the reason this is not folded into the ordinary script edit.
    ///
    /// It must be settable on its own, by a promotion pipeline that has just published a new
    /// version — otherwise every legitimate promotion looks like tampering at the next
    /// deployment. It must be clearable, so a script whose new hash nobody knows can be
    /// re-baselined by the next execution. And it must be audited, because a baseline that
    /// changes without a promotion behind it is exactly what an investigation would look for.
    /// </summary>
    [TestClass]
    public class ScriptContentHashRecordingTests
    {
        private const string ValidHash =
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        private List<Script> _stored = null!;
        private IDeploymentContext _context = null!;
        private IScriptsAuditPersistentSource _audit = null!;
        private ScriptsPersistentSource _source = null!;
        private IPrincipal _user = null!;

        [TestInitialize]
        public void Setup()
        {
            _stored = new List<Script>
            {
                new()
                {
                    Id = 1,
                    Name = "Deploy.ps1",
                    Path = "Deploy.ps1",
                    Components = new List<Component>()
                }
            };

            // Built into a local first: GetQueryableMockDbSet configures another substitute
            // internally, and NSubstitute cannot have that happen inside a Returns() argument.
            var scripts = DbContextMock.GetQueryableMockDbSet(_stored);

            _context = Substitute.For<IDeploymentContext>();
            _context.Scripts.Returns(scripts);
            _context.RecordScriptContentHashIfUnrecorded(
                    Arg.Any<int>(), Arg.Any<string>())
                .Returns(call =>
                {
                    var script = _stored.FirstOrDefault(candidate =>
                        candidate.Id == call.ArgAt<int>(0));

                    if (script == null || script.ContentHash != null)
                    {
                        return 0;
                    }

                    script.ContentHash = call.ArgAt<string>(1);
                    return 1;
                });

            var contextFactory = Substitute.For<IDeploymentContextFactory>();
            contextFactory.GetContext().Returns(_context);

            var claimsReader = Substitute.For<IClaimsPrincipalReader>();
            claimsReader.GetUserFullDomainName(Arg.Any<IPrincipal>()).Returns(@"CORP\promoter");

            _audit = Substitute.For<IScriptsAuditPersistentSource>();
            _user = Substitute.For<IPrincipal>();

            _source = new ScriptsPersistentSource(contextFactory, claimsReader, _audit);
        }

        [TestMethod]
        public void FirstUseRecordsOnlyWhileTheBaselineIsUnrecorded()
        {
            var authoritative =
                _source.RecordContentHashIfUnrecorded(1, ValidHash, _user);

            Assert.AreEqual(ValidHash, authoritative);
            Assert.AreEqual(ValidHash, _stored.Single().ContentHash);
            _audit.Received(1).AddRecord(
                1,
                "Deploy.ps1",
                "ContentHash=(unrecorded)",
                $"ContentHash={ValidHash}",
                @"CORP\promoter",
                "RecordFirstContentHash",
                Arg.Any<string>());
        }

        [TestMethod]
        public void FirstUseReturnsAConcurrentWinnersBaselineWithoutOverwritingIt()
        {
            const string losingCandidate =
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
            _context.RecordScriptContentHashIfUnrecorded(1, losingCandidate)
                .Returns(_ =>
                {
                    _stored.Single().ContentHash = ValidHash;
                    return 0;
                });

            var authoritative =
                _source.RecordContentHashIfUnrecorded(1, losingCandidate, _user);

            Assert.AreEqual(ValidHash, authoritative);
            Assert.AreEqual(ValidHash, _stored.Single().ContentHash);
            _audit.DidNotReceiveWithAnyArgs().AddRecord(
                default, default!, default!, default!, default!, default!, default!);
        }

        [TestMethod]
        public void RecordsAHashAgainstTheScript()
        {
            Assert.IsTrue(_source.RecordContentHash(1, ValidHash, _user));

            Assert.AreEqual(ValidHash, _stored.Single().ContentHash);
        }

        [TestMethod]
        public void AdministrativeRecordingCanDeliberatelyReplaceAnExistingBaseline()
        {
            const string replacement =
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
            _source.RecordContentHash(1, ValidHash, _user);

            Assert.IsTrue(_source.RecordContentHash(1, replacement, _user));

            Assert.AreEqual(replacement, _stored.Single().ContentHash);
        }

        /// <summary>
        /// A pipeline handing over an upper-case hash is not making a mistake, and a stored
        /// baseline that differed only in case from the one the runner computes would refuse a
        /// script that had never changed.
        /// </summary>
        [TestMethod]
        public void StoresAHashInOneCaseWhateverCaseItArrivesIn()
        {
            _source.RecordContentHash(1, ValidHash.ToUpperInvariant(), _user);

            Assert.AreEqual(ValidHash, _stored.Single().ContentHash);
        }

        /// <summary>
        /// Refused where it is recorded rather than discovered as a mismatch on the next
        /// deployment, when the report would say the script had changed and it had not.
        /// </summary>
        [TestMethod]
        [DataRow("not-a-hash")]
        [DataRow("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a0")]
        [DataRow("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08a")]
        [DataRow("zf86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
        public void RefusesAValueThatIsNotShapedLikeASha256Hash(string candidate)
        {
            Assert.IsFalse(_source.RecordContentHash(1, candidate, _user));

            Assert.IsNull(_stored.Single().ContentHash);
        }

        [TestMethod]
        public void RefusesAScriptThatDoesNotExist()
        {
            Assert.IsFalse(_source.RecordContentHash(99, ValidHash, _user));
        }

        /// <summary>
        /// Clearing means "re-record on next execution", not "stop verifying this one". There is
        /// no per-script opt-out, because a per-script opt-out is a per-script hole.
        /// </summary>
        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void ClearsTheRecordedHash(string? cleared)
        {
            _source.RecordContentHash(1, ValidHash, _user);

            Assert.IsTrue(_source.RecordContentHash(1, cleared, _user));
            Assert.IsNull(_stored.Single().ContentHash);
        }

        [TestMethod]
        public void AuditsTheChangeWithBothTheOldAndTheNewBaseline()
        {
            _source.RecordContentHash(1, ValidHash, _user);

            _audit.Received(1).AddRecord(
                1,
                "Deploy.ps1",
                Arg.Is<string>(from => from.Contains("(unrecorded)")),
                Arg.Is<string>(to => to.Contains(ValidHash)),
                @"CORP\promoter",
                "RecordContentHash",
                Arg.Any<string>());
        }

        /// <summary>
        /// A pipeline that re-promotes an unchanged artefact would otherwise fill the audit trail
        /// with entries saying nothing happened, which is how a trail stops being read.
        /// </summary>
        [TestMethod]
        public void RecordingTheSameHashAgainIsNotAudited()
        {
            _source.RecordContentHash(1, ValidHash, _user);
            _audit.ClearReceivedCalls();

            Assert.IsTrue(_source.RecordContentHash(1, ValidHash, _user));

            _audit.DidNotReceiveWithAnyArgs().AddRecord(
                default, default!, default!, default!, default!, default!, default!);
        }

        /// <summary>
        /// The general script edit changes what a script is called and how it runs. If it also
        /// re-baselined what the script is allowed to BE, an unrelated edit would silently
        /// approve whatever is on the share — which is the state an attacker with script-edit
        /// rights would want to reach.
        /// </summary>
        [TestMethod]
        public void AnOrdinaryScriptEditLeavesTheBaselineAlone()
        {
            _source.RecordContentHash(1, ValidHash, _user);

            _source.UpdateScript(new ScriptApiModel
            {
                Id = 1,
                Name = "Renamed.ps1",
                Path = "Renamed.ps1",
                PowerShellVersionNumber = "7.4",
                ContentHash = null
            }, _user);

            Assert.AreEqual(ValidHash, _stored.Single().ContentHash);
        }
    }

    /// <summary>
    /// The hash is computed in the shared model assembly precisely so that the API that records
    /// one and the two PowerShell runners that verify against it cannot disagree — they share no
    /// other code, and they compile for different frameworks.
    /// </summary>
    [TestClass]
    public class ScriptContentHashTests
    {
        [TestMethod]
        public void HashesTheKnownVectorForTheBytesOfAbc()
        {
            // SHA-256("abc"), the published test vector. Pinned so that a change of algorithm or
            // of hex formatting cannot pass unnoticed - every recorded baseline in the estate
            // would silently stop matching.
            Assert.AreEqual(
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                ScriptContentHash.Of(Encoding.ASCII.GetBytes("abc")));
        }

        /// <summary>
        /// Over bytes, never over decoded text. A file that differs only by a byte-order mark is
        /// a different file to PowerShell as well, and a text-based hash would call the two the
        /// same — while also making the answer depend on which framework decoded it.
        /// </summary>
        [TestMethod]
        public void ADifferenceOfOneByteIsADifferentHash()
        {
            var withoutMark = Encoding.UTF8.GetBytes("Write-Host 'x'");
            var withMark = Encoding.UTF8.GetPreamble()
                .Concat(withoutMark)
                .ToArray();

            Assert.AreNotEqual(ScriptContentHash.Of(withoutMark), ScriptContentHash.Of(withMark));
        }

        [TestMethod]
        public void MatchesIgnoringCaseAndSurroundingSpace()
        {
            var hash = ScriptContentHash.Of(Encoding.ASCII.GetBytes("abc"));

            Assert.IsTrue(ScriptContentHash.Matches($"  {hash.ToUpperInvariant()} ", hash));
        }

        /// <summary>
        /// An absent baseline is not a match. It is the unrecorded case, which the caller has to
        /// handle as its own outcome rather than as a passed or failed verification.
        /// </summary>
        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        public void AnAbsentBaselineNeverMatches(string? recorded)
        {
            Assert.IsFalse(ScriptContentHash.Matches(
                recorded!, ScriptContentHash.Of(Encoding.ASCII.GetBytes("abc"))));
        }
    }
}
