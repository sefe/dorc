using System.Reflection;
using Dorc.Core;
using Microsoft.AspNetCore.Mvc;

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
    /// These assertions close that gap: every mapped path must resolve to a real
    /// controller, and any sub-path must resolve to a real action route on it.
    /// </summary>
    [TestClass]
    public class ApiCallerEndpointRouteTests
    {
        private static Dictionary<Endpoints, string> EndpointPaths()
        {
            var field = typeof(ApiCaller).GetField("EndpointPaths",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            return (Dictionary<Endpoints, string>)field.GetValue(null)!;
        }

        private static Type? ControllerNamed(string name)
        {
            return typeof(Dorc.Api.Controllers.RefDataDatabasesController).Assembly
                .GetTypes()
                .FirstOrDefault(t => typeof(ControllerBase).IsAssignableFrom(t)
                                     && !t.IsAbstract
                                     && t.Name.Equals(name + "Controller", StringComparison.Ordinal));
        }

        private static IEnumerable<string> ActionRoutesOn(Type controller)
        {
            return controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetCustomAttributes<RouteAttribute>())
                .Select(r => r.Template)
                .Where(t => !string.IsNullOrEmpty(t))!;
        }

        [TestMethod]
        public void EveryMappedEndpointPath_ResolvesToAController()
        {
            foreach (var (endpoint, path) in EndpointPaths())
            {
                var controllerName = path.Split('/')[0];

                Assert.IsNotNull(ControllerNamed(controllerName),
                    $"ApiCaller maps {endpoint} to '{path}', but no {controllerName}Controller exists.");
            }
        }

        [TestMethod]
        public void EveryMappedSubPath_ResolvesToAnActionRoute()
        {
            foreach (var (endpoint, path) in EndpointPaths())
            {
                var segments = path.Split('/');
                if (segments.Length == 1)
                    continue;

                var controller = ControllerNamed(segments[0])!;
                var subPath = string.Join('/', segments.Skip(1));

                CollectionAssert.Contains(ActionRoutesOn(controller).ToList(), subPath,
                    $"ApiCaller maps {endpoint} to '{path}', but {controller.Name} has no action routed at '{subPath}'.");
            }
        }
    }
}
