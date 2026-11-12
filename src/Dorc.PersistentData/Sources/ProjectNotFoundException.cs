namespace Dorc.PersistentData.Sources;

public sealed class ProjectNotFoundException : KeyNotFoundException
{
    public ProjectNotFoundException(int projectId)
        : base($"Project {projectId} was not found.")
    {
    }
}
