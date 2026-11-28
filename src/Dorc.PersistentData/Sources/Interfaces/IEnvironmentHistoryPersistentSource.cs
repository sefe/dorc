using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IEnvironmentHistoryPersistentSource
    {
        bool UpdateHistory(string envName, string backupFile, string comment, string updatedBy,
            string updateType);

        List<EnvironmentHistoryApiModel> GetEnvironmentDetailHistory(int envId);
        string? GetEnvironmentNameForHistoryId(int id);
        void UpdateEnvironmentDetailHistoryComment(int id, string comment);
    }
}