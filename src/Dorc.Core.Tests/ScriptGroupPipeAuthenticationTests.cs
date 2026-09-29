using Dorc.TerraformRunner.Pipes;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// The script group pipe carries every resolved deployment property and three first-class
    /// secrets. Its name is composed from the machine name and the request id and is placed on
    /// the Runner's own command line, so it is not a secret and whoever creates it first owns
    /// it.
    ///
    /// Server-side access control stops another local process READING the script group. It
    /// does nothing about the opposite direction: a process that wins the creation race serves
    /// a script group of its own authorship, and ScriptsLocation and ScriptPath in it are
    /// arbitrary code as the deployment account. Only the client checking who owns the pipe
    /// closes that, which is what these cover.
    ///
    /// The checks are exercised through the Terraform Runner's client; the .NET and .NET
    /// Framework Runners carry the same two methods verbatim.
    /// </summary>
    [TestClass]
    public class ScriptGroupPipeAuthenticationTests
    {
        private const string MonitorSid = "S-1-5-21-1111111111-2222222222-3333333333-1001";
        private const string SquatterSid = "S-1-5-21-1111111111-2222222222-3333333333-1002";
        private const string AdministratorsSid = "S-1-5-32-544";

        [TestMethod]
        public void AcceptsAPipeOwnedByTheStatedServer()
        {
            // No exception is the acceptance.
            ScriptGroupPipeClient.RequireOwnedBy(MonitorSid, MonitorSid, "DOrcMonitor-host-42");
        }

        /// <summary>
        /// The squatter case, directly. Before this check the Runner deserialised whatever was
        /// on the pipe and executed the script paths it named.
        /// </summary>
        [TestMethod]
        public void RefusesAPipeOwnedByAnyoneElse()
        {
            var refusal = Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                ScriptGroupPipeClient.RequireOwnedBy(
                    MonitorSid, SquatterSid, "DOrcMonitor-host-42"));

            StringAssert.Contains(refusal.Message, SquatterSid);
            StringAssert.Contains(refusal.Message, "Refusing to accept a script group");
        }

        /// <summary>
        /// An admin group owning the pipe is not the same as the Monitor owning it - anyone who
        /// can become a local administrator could then serve script groups. The server sets the
        /// owner explicitly for this reason, rather than letting it default to the creating
        /// token's owner, which for an account in Administrators is the group.
        /// </summary>
        [TestMethod]
        public void RefusesAPipeOwnedByAdministratorsRatherThanTheServerAccount()
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                ScriptGroupPipeClient.RequireOwnedBy(MonitorSid, AdministratorsSid, "pipe"));
        }

        /// <summary>
        /// A Runner that was never told whom to trust must stop, not proceed. Treating an
        /// absent expectation as "no check required" would leave the weakness open wherever the
        /// argument failed to be passed - which is every place it matters most.
        /// </summary>
        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void RefusesWhenNoServerIdentityWasStated(string? statedSid)
        {
            var refusal = Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                ScriptGroupPipeClient.RequireOwnedBy(statedSid!, MonitorSid, "pipe"));

            StringAssert.Contains(refusal.Message, "cannot be authenticated");
        }

        /// <summary>
        /// A malformed statement is not a licence to skip the check. It cannot match a real
        /// owner, so it refuses - which is the same outcome as an absent one, reached without
        /// needing to parse anything.
        /// </summary>
        [TestMethod]
        [DataRow("not-a-sid")]
        [DataRow("S-1-5-21-")]
        [DataRow("CORP\\svc-dorc")]
        public void RefusesWhenTheStatedServerIdentityIsMalformed(string statedSid)
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                ScriptGroupPipeClient.RequireOwnedBy(statedSid, MonitorSid, "pipe"));
        }

        /// <summary>
        /// Surrounding whitespace comes from the command line the Monitor composes, not from
        /// anything meaningful about the identity.
        /// </summary>
        [TestMethod]
        public void IgnoresSurroundingWhitespaceInTheStatedIdentity()
        {
            ScriptGroupPipeClient.RequireOwnedBy($"  {MonitorSid} ", MonitorSid, "pipe");
        }
    }
}
