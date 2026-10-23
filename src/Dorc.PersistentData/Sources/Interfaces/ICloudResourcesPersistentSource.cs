using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface ICloudResourcesPersistentSource : ISharedResourceMutationSource
    {
        IEnumerable<CloudResourceApiModel> GetAll();
        CloudResourceApiModel? GetById(int id);
        CloudResourceApiModel? GetByName(string name);
        /// <summary>Returns every matching remote identity, including ambiguous legacy duplicates.</summary>
        IReadOnlyList<CloudResourceApiModel> GetByIdentity(string provider, string resourceType, string resourceIdentifier);
        CloudResourceApiModel Add(CloudResourceApiModel cloudResource);
        CloudResourceApiModel? Update(int id, CloudResourceApiModel cloudResource);
        CloudResourceApiModel? UpdateTags(int id, string? tags);
        bool Delete(int id);
        IEnumerable<CloudResourceApiModel> GetForEnvironmentId(int environmentId);
        IEnumerable<string> GetEnvironmentNamesForId(int id);
        EnvironmentAttachmentOutcome AttachToEnvironment(int id, int environmentId);
        EnvironmentAttachmentOutcome DetachFromEnvironment(int id, int environmentId);
    }
}
