using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{
    public class ComponentsPersistentSource : IComponentsPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public ComponentsPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public ComponentApiModel? GetComponentByName(string componentName)
        {
            using (var context = _contextFactory.GetContext())
            {
                var component = context.Components
                    .Include(component => component.Script)
                    .Include(component => component.Parent)
                    .Where(component => component.Name == componentName).ToList()
                    .Select(component => MapToComponentApiModel(component, context))
                    .SingleOrDefault();

                return component;
            }
        }

        public void LoadChildren(ComponentApiModel componentApiModel)
        {
            using (var context = _contextFactory.GetContext())
            {
                var componentChildren = context.Components
                    .Include(component => component.Script)
                    .Include(component => component.Parent)
                    .Where(component =>
                        component.Parent != null
                        && component.Parent.Id == componentApiModel.ComponentId)
                    .Select(component => MapToComponentApiModel(component, context))
                    .Where(c => c != null).Cast<ComponentApiModel>().ToList();

                componentApiModel.Children = componentChildren;
            }
        }

        public void SaveEnvComponentStatus(int environmentId, ComponentApiModel component,
            string resultStatus, int requestId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var environmentComponentStatus =
                    context.EnvironmentComponentStatuses.FirstOrDefault(
                        x => x.Environment.Id == environmentId
                        && x.Component.Id == component.ComponentId);

                if (environmentComponentStatus == null)
                {
                    environmentComponentStatus = new EnvironmentComponentStatus
                    {
                        Environment = context.Environments.First(environment => environment.Id == environmentId),
                        Component = context.Components.First(c => c.Id == component.ComponentId)
                    };
                    context.EnvironmentComponentStatuses.Add(environmentComponentStatus);
                }

                environmentComponentStatus.Status = resultStatus;
                environmentComponentStatus.UpdateDate = DateTimeOffset.Now;
                environmentComponentStatus.DeploymentRequest = context.DeploymentRequests.First(dr => dr.Id == requestId);
                context.SaveChanges();
            }
        }

        public ScriptApiModel? GetScripts(int componentId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var firstComponentWithSpecifiedId = context.Components
                    .Include(component => component.Script)
                    .First(component => component.Id == componentId);


                var script = firstComponentWithSpecifiedId.Script;

                return script is null ? null : MapToScriptApiModel(script);
            }
        }

        private static ScriptApiModel MapToScriptApiModel(Script script)
        {
            return new ScriptApiModel
            {
                Id = script.Id,
                Name = script.Name,
                Path = script.Path,
                IsPathJSON = script.IsPathJSON,
                NonProdOnly = script.NonProdOnly,
                PowerShellVersionNumber = script.PowerShellVersionNumber
            };
        }

        internal static ComponentApiModel? MapToComponentApiModel(Component component, IDeploymentContext context)
        {
            if (component is null) return null;

            context.Entry(component)
                .Reference(component => component.Parent)
                .Load();

            var output = new ComponentApiModel
            {
                Children = new List<ComponentApiModel>(),
                ComponentId = component.Id,
                ComponentName = component.Name,
                ScriptPath = component.Script?.Path ?? "",
                ParentId = component.Parent?.Id ?? 0,
                IsEnabled = component.IsEnabled
            };

            context.Entry(component)
                .Collection(b => b.Children)
                .Load();

            foreach (var child in component.Children)
            {
                output.Children.Add(MapToComponentApiModel(child, context));
            }

            return output;
        }
    }
}