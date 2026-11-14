using Dorc.Api.Controllers;
using Dorc.Core.VariableResolution;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class ScriptingDependencyAbsenceTests
    {
        [TestMethod]
        public void ApiAndCoreAssembliesDoNotReferenceCSharpScripting()
        {
            foreach (var assembly in new[] { typeof(ConfigValuesController).Assembly, typeof(PropertyExpressionGrammar).Assembly })
            {
                var referenced = assembly
                    .GetReferencedAssemblies()
                    .Select(name => name.Name ?? string.Empty)
                    .ToList();

                Assert.IsFalse(
                    referenced.Any(name => name.Contains("CodeAnalysis", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Scripting", StringComparison.OrdinalIgnoreCase)),
                    $"{assembly.GetName().Name} references a scripting compiler assembly: {string.Join(", ", referenced)}");
            }
        }

        [TestMethod]
        public void ApiDepsJsonDoesNotContainCSharpScripting()
        {
            var apiDepsPath = Path.Join(AppContext.BaseDirectory, "Dorc.Api.deps.json");
            var testDepsPath = Path.Join(AppContext.BaseDirectory, "Dorc.Api.Tests.deps.json");

            Assert.IsTrue(File.Exists(apiDepsPath), $"Expected '{apiDepsPath}' to exist.");
            Assert.IsTrue(File.Exists(testDepsPath), $"Expected '{testDepsPath}' to exist.");

            foreach (var depsPath in new[] { apiDepsPath, testDepsPath })
            {
                var content = File.ReadAllText(depsPath);
                Assert.IsFalse(
                    content.Contains("Microsoft.CodeAnalysis.CSharp.Scripting", StringComparison.OrdinalIgnoreCase)
                    || content.Contains("Microsoft.CodeAnalysis.Scripting", StringComparison.OrdinalIgnoreCase),
                    $"'{Path.GetFileName(depsPath)}' still references Microsoft.CodeAnalysis.*Scripting.");
            }

            var scriptingDlls = Directory.GetFiles(AppContext.BaseDirectory, "Microsoft.CodeAnalysis*Scripting*.dll");
            Assert.AreEqual(0, scriptingDlls.Length,
                "Scripting compiler DLLs present in Api output: " + string.Join(", ", scriptingDlls));
        }
    }
}
