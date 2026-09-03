namespace Dorc.Core.Tests
{
    [TestClass]
    public class VariableScopeOptionsResolverTests
    {
        [TestMethod]
        [DataRow("IAR DV 07", "DV07")]
        [DataRow("IAR QA 12", "QA12")]
        [DataRow("IAR QA 01", "QA01")]
        [DataRow("Endur DV 10", "DV10")]
        [DataRow("IAR DV 11", "DV11")]
        [DataRow("DOrc DV 03", "DV03")]
        [DataRow("TEVO DV 11", "DV11")]
        [DataRow("IDF DV 01", "DV01")]
        [DataRow("Endur v18 PR", "v18PR")]
        [DataRow("Endur Sandbox Testing", "SandboxTesting")]
        [DataRow("Endur-QA-27", "QA27")]
        [DataRow("DV 07", "DV07")]
        [DataRow("SingleWord", "SingleWord")]
        [DataRow("local", "local")]
        public void GetShortNameFromEnvironmentName_ReturnsExpectedShortName(string environmentName, string expected)
        {
            var result = VariableScopeOptionsResolver.GetShortNameFromEnvironmentName(environmentName);
            Assert.AreEqual(expected, result);
        }
    }
}
