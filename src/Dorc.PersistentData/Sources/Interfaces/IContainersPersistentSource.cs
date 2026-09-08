using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IContainersPersistentSource
    {
        IEnumerable<ContainerApiModel> GetAll();
        ContainerApiModel? GetById(int id);
        ContainerApiModel? GetByName(string name);
        ContainerApiModel Add(ContainerApiModel container);
        ContainerApiModel? Update(int id, ContainerApiModel container);
        bool Delete(int id);
        IEnumerable<ContainerApiModel> GetForEnvironmentId(int environmentId);
        IEnumerable<string> GetEnvironmentNamesForId(int id);
        EnvironmentAttachmentOutcome AttachToEnvironment(int id, int environmentId);
        EnvironmentAttachmentOutcome DetachFromEnvironment(int id, int environmentId);
    }
}
