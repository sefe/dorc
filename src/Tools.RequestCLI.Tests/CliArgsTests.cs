using System;
using Tools.RequestCLI;

namespace Tools.RequestCLI.Tests
{
    [TestClass]
    public class CliArgsTests
    {
        [TestMethod]
        public void BuildUri_WithPortAndPath_IsPreservedInFull()
        {
            var cliArgs = new CliArgs(new[] { "/builduri:https://buildserver:8080/build/123" });

            Assert.AreEqual("https://buildserver:8080/build/123", cliArgs.Request.BuildUrl);
        }

        [TestMethod]
        public void BuildUri_WithoutPort_IsPreserved()
        {
            var cliArgs = new CliArgs(new[] { "/builduri:https://dev.azure.com/org/project/_build" });

            Assert.AreEqual("https://dev.azure.com/org/project/_build", cliArgs.Request.BuildUrl);
        }

        [TestMethod]
        public void BuildUri_WithSurroundingWhitespace_IsTrimmed()
        {
            var cliArgs = new CliArgs(new[] { "/builduri: https://host/path " });

            Assert.AreEqual("https://host/path", cliArgs.Request.BuildUrl);
        }

        [TestMethod]
        public void BuildUri_WithNoValue_ThrowsArgumentException()
        {
            Assert.ThrowsExactly<ArgumentException>(() => new CliArgs(new[] { "/builduri:" }));
        }

        [TestMethod]
        public void BuildUri_WithWhitespaceOnlyValue_ThrowsArgumentException()
        {
            Assert.ThrowsExactly<ArgumentException>(() => new CliArgs(new[] { "/builduri:   " }));
        }

        [TestMethod]
        public void ParseArguments_PopulatesScalarFields()
        {
            var cliArgs = new CliArgs(new[]
            {
                "/project:MyProject",
                "/targetenv:DEV",
                "/buildtext:Release",
                "/buildnum:42",
                "/pinned:true",
                "/wait:true"
            });

            Assert.AreEqual("MyProject", cliArgs.Request.Project);
            Assert.AreEqual("DEV", cliArgs.Request.Environment);
            Assert.AreEqual("Release", cliArgs.Request.BuildText);
            Assert.AreEqual("42", cliArgs.Request.BuildNum);
            Assert.AreEqual(true, cliArgs.Request.Pinned);
            Assert.IsTrue(cliArgs.Wait);
        }

        [TestMethod]
        public void ParseArguments_ComponentsAreSplitOnSemicolon()
        {
            var cliArgs = new CliArgs(new[] { "/components:Web;Api;Db" });

            CollectionAssert.AreEqual(new[] { "Web", "Api", "Db" }, cliArgs.Request.Components.ToArray());
        }
    }
}
