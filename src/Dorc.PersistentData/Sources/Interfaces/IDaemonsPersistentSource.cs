using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IDaemonsPersistentSource
    {
        IEnumerable<DaemonApiModel> GetDaemonsForServer(int serverId);
        IEnumerable<DaemonApiModel> GetDaemons();
        DaemonApiModel? GetDaemonById(int daemonId);
        DaemonApiModel Add(DaemonApiModel daemonApiModel);
        bool Delete(int daemonApiModelId);
        DaemonApiModel Update(DaemonApiModel env);
        bool AttachDaemonToServer(int serverId, int daemonId);
        bool DetachDaemonFromServer(int serverId, int daemonId);
    }
}