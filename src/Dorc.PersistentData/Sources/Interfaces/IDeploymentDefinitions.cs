using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces;

public interface IDeploymentDefinitions
{
    RefDataApiModel Get(int projectId);
    RefDataApiModel Save(RefDataApiModel input, bool create, string actor,
        Func<int, bool> canModify, Action validate, bool projectOnly = false);
    bool EditScript(ScriptApiModel input, string actor, Func<int, bool> canModify);
    ProjectApiModel CreateProject(ProjectApiModel input, string actor, AccessControlApiModel owner, Action validate);
    void DeleteProject(int projectId, string expectedRevision, string actor, Func<int, bool> canModify);
}
