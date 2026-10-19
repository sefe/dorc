using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IApiRegistrationsPersistentSource : ISharedResourceMutationSource
    {
        IEnumerable<ApiRegistrationApiModel> GetAll();
        ApiRegistrationApiModel? GetById(int id);
        ApiRegistrationApiModel? GetByName(string name);
        ApiRegistrationApiModel Add(ApiRegistrationApiModel apiRegistration);
        ApiRegistrationApiModel? Update(int id, ApiRegistrationApiModel apiRegistration);
        ApiRegistrationApiModel? UpdateTags(int id, string? tags);
        bool Delete(int id);
        IEnumerable<ApiRegistrationApiModel> GetForEnvironmentId(int environmentId);
        IEnumerable<string> GetEnvironmentNamesForId(int id);
        EnvironmentAttachmentOutcome AttachToEnvironment(int id, int environmentId);
        EnvironmentAttachmentOutcome DetachFromEnvironment(int id, int environmentId);
    }
}
