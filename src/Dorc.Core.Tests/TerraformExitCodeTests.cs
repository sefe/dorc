using Dorc.TerraformRunner;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class TerraformExitCodeTests
    {
        // plan -detailed-exitcode: 0 = no changes, 2 = success WITH changes, 1 = error.
        [DataTestMethod]
        [DataRow(0, true)]   // no changes
        [DataRow(2, true)]   // success with changes — the normal case for a real deployment
        [DataRow(1, false)]  // error
        [DataRow(3, false)]  // unexpected
        public void PlanWithDetailedExitCode_TreatsTwoAsSuccess(int exitCode, bool expected)
        {
            var args = "plan -detailed-exitcode -out=plan.tfplan";
            Assert.AreEqual(expected, TerraformProcessor.IsTerraformCommandSuccessful(exitCode, args));
        }

        // apply/init/show (no -detailed-exitcode): only 0 is success.
        [DataTestMethod]
        [DataRow(0, true)]
        [DataRow(1, false)]
        [DataRow(2, false)]            // a plain apply returning 2 is NOT success
        [DataRow(3, false)]
        [DataRow(-1073741819, false)]  // access violation crash
        [DataRow(137, false)]          // OOM-killed
        public void ApplyWithoutDetailedExitCode_FailsOnAnyNonZero(int exitCode, bool expected)
        {
            var args = "apply -auto-approve plan.tfplan";
            Assert.AreEqual(expected, TerraformProcessor.IsTerraformCommandSuccessful(exitCode, args));
        }
    }
}
