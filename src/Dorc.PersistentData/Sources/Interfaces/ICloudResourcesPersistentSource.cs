using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface ICloudResourcesPersistentSource
    {
        IEnumerable<CloudResourceApiModel> GetAll();
        CloudResourceApiModel? GetById(int id);
        CloudResourceApiModel? GetByName(string name);
        CloudResourceApiModel Add(CloudResourceApiModel cloudResource);
        CloudResourceApiModel? Update(int id, CloudResourceApiModel cloudResource);
        bool Delete(int id);
        IEnumerable<CloudResourceApiModel> GetForEnvironmentId(int environmentId);
        IEnumerable<string> GetEnvironmentNamesForId(int id);
        EnvironmentAttachmentOutcome AttachToEnvironment(int id, int environmentId);
        EnvironmentAttachmentOutcome DetachFromEnvironment(int id, int environmentId);
    }
}
