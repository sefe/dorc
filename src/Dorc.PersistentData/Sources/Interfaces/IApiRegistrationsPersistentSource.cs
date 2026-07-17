using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IApiRegistrationsPersistentSource
    {
        IEnumerable<ApiRegistrationApiModel> GetAll();
        ApiRegistrationApiModel? GetById(int id);
        ApiRegistrationApiModel? GetByName(string name);
        ApiRegistrationApiModel Add(ApiRegistrationApiModel apiRegistration);
        ApiRegistrationApiModel? Update(int id, ApiRegistrationApiModel apiRegistration);
        bool Delete(int id);
        IEnumerable<ApiRegistrationApiModel> GetForEnvironmentId(int environmentId);
        IEnumerable<string> GetEnvironmentNamesForId(int id);
        EnvironmentAttachmentOutcome AttachToEnvironment(int id, int environmentId);
        EnvironmentAttachmentOutcome DetachFromEnvironment(int id, int environmentId);
    }
}
