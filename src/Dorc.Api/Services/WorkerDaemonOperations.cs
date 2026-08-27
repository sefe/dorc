using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Interfaces;

namespace Dorc.Api.Services
{
    // Primary-side implementation of the S-005 seam: forwards daemon probing and service
    // control to the Windows worker. Synchronous by contract — IDaemonStatusProbe and its
    // controller surface are sync — so the async worker client is awaited inline.
    public class WorkerDaemonOperations : IDaemonOperations
    {
        private readonly IWindowsWorkerClient _workerClient;

        public WorkerDaemonOperations(IWindowsWorkerClient workerClient)
        {
            _workerClient = workerClient;
        }

        public List<DaemonStatus> ProbeStatuses(WorkerDaemonCredentialApiModel? credential, List<DaemonStatus> daemons)
        {
            var request = new WorkerDaemonProbeRequestApiModel
            {
                Credential = credential,
                Daemons = daemons.Select(ToWire).ToList()
            };

            var probed = _workerClient.ProbeDaemonStatusesAsync(request).GetAwaiter().GetResult();

            return probed.Select(FromWire).ToList();
        }

        public DaemonStatus? ChangeState(WorkerDaemonCredentialApiModel? credential, DaemonStatus daemonStatus)
        {
            var request = new WorkerDaemonStateChangeRequestApiModel
            {
                Credential = credential,
                Daemon = ToWire(daemonStatus)
            };

            var result = _workerClient.ChangeDaemonStateAsync(request).GetAwaiter().GetResult();

            return result == null ? null : FromWire(result);
        }

        private static WorkerDaemonApiModel ToWire(DaemonStatus status) => new()
        {
            EnvName = status.EnvName,
            ServerName = status.ServerName,
            DaemonName = status.DaemonName,
            Status = status.Status,
            ErrorMessage = status.ErrorMessage,
            ServerId = status.ServerId,
            DaemonId = status.DaemonId
        };

        private static DaemonStatus FromWire(WorkerDaemonApiModel model) => new()
        {
            EnvName = model.EnvName,
            ServerName = model.ServerName,
            DaemonName = model.DaemonName,
            Status = model.Status,
            ErrorMessage = model.ErrorMessage,
            ServerId = model.ServerId,
            DaemonId = model.DaemonId
        };
    }
}
