using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IDaemonsPersistentSource
    {
        IEnumerable<DaemonApiModel> GetDaemonsForServer(int serverId);
        IEnumerable<DaemonApiModel> GetDaemons();
        DaemonApiModel Add(DaemonApiModel daemonApiModel);
        bool Delete(int daemonApiModelId);
        DaemonApiModel? Update(DaemonApiModel env);
    }
}