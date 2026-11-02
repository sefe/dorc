using System.Reflection;
using Dorc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Dorc.Api.Tests.Controllers
{
    /// <summary>
    /// ApiCaller maps each <see cref="Endpoints"/> value to a literal URL path, and
    /// the in-process CLI tools call the API through that map. Nothing in the compiler
    /// connects those strings to the [Route] attributes they are supposed to address,
    /// so renaming a controller route leaves the map pointing at a 404 and every test
    /// still passes — which is exactly what happened when RefDataDatabases/ByType
    /// became ByTag.
    ///
    /// These assertions close that gap: every mapped path must match a route a
    /// controller actually serves, prefix and all.
    ///
    /// Routes are read from the attributes rather than inferred from class names. A
    /// controller's own [Route] template is what decides its prefix — a controller
    /// routed at "api/[controller]" does not answer on "Properties" however it is
    /// named — and action routes come from any IRouteTemplateProvider, so
    /// [HttpGet("ByTag")] counts the same as [HttpGet] + [Route("ByTag")]. Both
    /// styles are in use in this codebase.
    ///
    /// Path-only. The other half of the same coupling — the query-key names callers
    /// build their dictionaries from, e.g. RefreshEndur's { "tag", "Endur" } against
    /// GetByTag(string tag) — is not checked here, because the keys are string
    /// literals at each call site rather than anything this test can enumerate. A
    /// mismatch there fails as a 400 rather than a 404, so it is quieter still;
    /// closing it properly means giving ApiCaller a typed request surface.
    /// </summary>
    [TestClass]
    public class ApiCallerEndpointRouteTests
    {
        private static Dictionary<Endpoints, string> EndpointPaths()
        {
            var field = typeof(ApiCaller).GetField("EndpointPaths",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(field,
                "ApiCaller.EndpointPaths was renamed or removed - this test reads it by reflection.");

            return (Dictionary<Endpoints, string>)field.GetValue(null)!;
        }

        /// <summary>
        /// Every route a controller in Dorc.Api actually serves, as a full path with
        /// the controller prefix applied. A controller with no [Route] is not
        /// reachable by a fixed path and contributes nothing.
        /// </summary>
        private static HashSet<string> ServedRoutes()
        {
            var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var controllers = typeof(Dorc.Api.Controllers.RefDataDatabasesController).Assembly
                .GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

            foreach (var controller in controllers)
            {
                var name = controller.Name.EndsWith("Controller", StringComparison.Ordinal)
                    ? controller.Name[..^"Controller".Length]
                    : controller.Name;

                var prefixes = controller.GetCustomAttributes<RouteAttribute>()
                    .Select(r => r.Template.Replace("[controller]", name, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (prefixes.Count == 0)
                    continue;

                var actionTemplates = controller
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(m => m.GetCustomAttributes().OfType<IRouteTemplateProvider>())
                    .Select(r => r.Template)
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Distinct();

                foreach (var prefix in prefixes)
                {
                    routes.Add(prefix.Trim('/'));
                    foreach (var template in actionTemplates)
                        routes.Add($"{prefix.Trim('/')}/{template!.Trim('/')}");
                }
            }

            return routes;
        }

        [TestMethod]
        public void EveryMappedEndpointPath_IsServedByAController()
        {
            var served = ServedRoutes();

            foreach (var (endpoint, path) in EndpointPaths())
            {
                Assert.IsTrue(served.Contains(path.Trim('/')),
                    $"ApiCaller maps {endpoint} to '{path}', which no controller in Dorc.Api serves.");
            }
        }

        [TestMethod]
        public void ByTag_QueryKeysMatchTheActionParameters()
        {
            // The other half of the coupling. RefreshEndur builds its query dictionary
            // from the string literals "envName" and "tag"; nothing connects them to
            // GetByTag's parameter names. Renaming a parameter turns the call into a
            // 400 with a null needle rather than a 404, so it is quieter than the path
            // mismatch above and would not be caught by it.
            var action = typeof(Dorc.Api.Controllers.RefDataDatabasesController)
                .GetMethod(nameof(Dorc.Api.Controllers.RefDataDatabasesController.GetByTag));

            Assert.IsNotNull(action, "RefDataDatabasesController.GetByTag should exist");

            var parameterNames = action!.GetParameters().Select(p => p.Name).ToArray();

            CollectionAssert.AreEquivalent(new[] { "envName", "tag" }, parameterNames,
                "RefreshEndur.GetEndurDatabase sends exactly these query keys; changing a "
                + "parameter name here silently breaks the post-restore Endur refresh.");
        }
    }
}
