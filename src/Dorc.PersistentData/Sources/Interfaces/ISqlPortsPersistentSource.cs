using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface ISqlPortsPersistentSource
    {
        IEnumerable<SqlPortApiModel> GetSqlPorts();
        string GetSqlPort(string targetInstance);
    }
}