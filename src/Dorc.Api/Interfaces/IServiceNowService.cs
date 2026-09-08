using Dorc.Api.Models;

namespace Dorc.Api.Interfaces
{
    public interface IServiceNowService
    {
        Task<ChangeRequestValidationResult> ValidateChangeRequestAsync(string crNumber);
        Task<CreateChangeRequestResult> CreateChangeRequestAsync(CreateChangeRequestInput input);
    }
}
