namespace Dorc.Api.Interfaces
{
    public interface IServiceNowService
    {
        Task<ChangeRequestValidationResult> ValidateChangeRequestAsync(string crNumber);
        Task<CreateChangeRequestResult> CreateChangeRequestAsync(CreateChangeRequestInput input);
    }
}
