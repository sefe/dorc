using Dorc.ApiModel;

namespace Dorc.Api.WindowsWorker.Services
{
    public interface IDaemonServiceOperations
    {
        List<WorkerDaemonApiModel> Probe(
            WorkerDaemonCredentialApiModel? credential,
            List<WorkerDaemonApiModel> daemons);

        WorkerDaemonApiModel? ChangeState(
            WorkerDaemonCredentialApiModel? credential,
            WorkerDaemonApiModel daemon);
    }
}
