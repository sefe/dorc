using Dorc.ApiModel;
using Dorc.Core.DatabaseAccess;
using Dorc.Core.Interfaces;
using NSubstitute;

namespace Dorc.Core.Tests.DatabaseAccess;

[TestClass]
public class DatabaseAccessDirectoryTests
{
    [TestMethod]
    public void SearchAndResolve_ValidateInputsPreferPidTranslateSidsAndHandleNonWindows()
    {
        var ad = Substitute.For<IActiveDirectorySearcher>();
        var windowsDir = new DatabaseAccessDirectory(ad);
        var nonWindowsDir = new DatabaseAccessDirectory(ad, () => false);

        Assert.ThrowsExactly<ArgumentException>(() => windowsDir.Search(""));
        Assert.ThrowsExactly<ArgumentException>(() => windowsDir.Search("ab"));
        Assert.ThrowsExactly<ArgumentException>(() => windowsDir.Search(new string('x', 101)));

        ad.Search("svc").Returns([
            new UserElementApiModel { Pid = "PID-1", Sid = "S-1-5-18", DisplayName = "WithPid", IsGroup = false },
            new UserElementApiModel { Pid = "", Sid = "S-1-5-18", DisplayName = "LocalSystem", IsGroup = true }
        ]);

        var nonWinResults = nonWindowsDir.Search("svc");
        Assert.HasCount(2, nonWinResults);
        Assert.AreEqual("PID-1", nonWinResults[0].Id);
        Assert.IsNull(nonWinResults[0].AccountName);
        Assert.AreEqual("Resolved", nonWinResults[0].Status);

        if (OperatingSystem.IsWindows())
        {
            var winResults = windowsDir.Search("svc");
            Assert.IsNull(winResults[0].AccountName); // "PID-1" is not a valid SID -> catches SystemException and returns null
            Assert.IsFalse(string.IsNullOrWhiteSpace(winResults[1].AccountName));
        }

        // Missing stable identity throws
        ad.Search("bad").Returns([new UserElementApiModel { Pid = "", Sid = " " }]);
        Assert.ThrowsExactly<ArgumentException>(() => windowsDir.Search("bad"));

        // Resolve validation and mismatched identity rejection
        Assert.ThrowsExactly<ArgumentException>(() => windowsDir.Resolve(""));
        Assert.ThrowsExactly<ArgumentException>(() => windowsDir.Resolve(new string('s', 257)));

        ad.GetUserDataById("S-expected").Returns(new UserElementApiModel { Sid = "S-different", DisplayName = "Other" });
        Assert.ThrowsExactly<ArgumentException>(() => windowsDir.Resolve("S-expected"));

        ad.GetUserDataById("S-1-5-18").Returns(new UserElementApiModel { Sid = "S-1-5-18", DisplayName = "System", IsGroup = false });
        var resolved = nonWindowsDir.Resolve("S-1-5-18");
        Assert.AreEqual("S-1-5-18", resolved.Id);
        Assert.AreEqual("System", resolved.DisplayName);
    }
}
