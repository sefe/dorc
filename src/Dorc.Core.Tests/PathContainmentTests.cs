using System;
using System.IO;
using Dorc.ApiModel;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class PathContainmentTests
    {
        // A relative root; PathContainment canonicalises it via Path.GetFullPath internally.
        private const string Root = "dorc-containment-root";

        [TestMethod]
        public void ResolveWithinRoot_PlainFileName_ReturnsContainedPath()
        {
            var resolved = PathContainment.ResolveWithinRoot(Root, "scriptgroup.json");

            StringAssert.StartsWith(resolved, Path.GetFullPath(Root));
            StringAssert.EndsWith(resolved, "scriptgroup.json");
        }

        [TestMethod]
        public void ResolveWithinRoot_Subdirectory_ReturnsContainedPath()
        {
            var resolved = PathContainment.ResolveWithinRoot(Root, "sub/file.json");

            StringAssert.StartsWith(resolved, Path.GetFullPath(Root));
        }

        [TestMethod]
        public void ResolveWithinRoot_ParentTraversal_Throws()
        {
            Assert.Throws<UnauthorizedAccessException>(() =>
                PathContainment.ResolveWithinRoot(Root, "../escape.json"));
        }

        [TestMethod]
        public void ResolveWithinRoot_DeepParentTraversal_Throws()
        {
            Assert.Throws<UnauthorizedAccessException>(() =>
                PathContainment.ResolveWithinRoot(Root, "../../../../etc/passwd"));
        }

        [TestMethod]
        public void ResolveWithinRoot_AbsolutePathOverride_Throws()
        {
            // An absolute/rooted second argument overrides the root via Path.Combine.
            Assert.Throws<UnauthorizedAccessException>(() =>
                PathContainment.ResolveWithinRoot(Root, Path.GetTempPath()));
        }

        [TestMethod]
        public void ResolveWithinRoot_NullOrEmpty_Throws()
        {
            Assert.Throws<ArgumentException>(() => PathContainment.ResolveWithinRoot(Root, null));
            Assert.Throws<ArgumentException>(() => PathContainment.ResolveWithinRoot(Root, ""));
            Assert.Throws<ArgumentException>(() => PathContainment.ResolveWithinRoot(null, "x"));
        }
    }
}
