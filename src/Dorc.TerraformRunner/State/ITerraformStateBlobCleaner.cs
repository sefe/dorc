using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.TerraformRunner.State
{
    public interface ITerraformStateBlobCleaner
    {
        Task<bool> DeleteStateBlobIfConfiguredAsync(
            ScriptGroup? scriptGroup,
            CancellationToken cancellationToken = default);
    }
}
