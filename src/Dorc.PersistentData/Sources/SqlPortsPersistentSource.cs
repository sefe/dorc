using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Data.SqlClient;

namespace Dorc.PersistentData.Sources
{
    public class SqlPortsPersistentSource : ISqlPortsPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public SqlPortsPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public IEnumerable<SqlPortApiModel> GetSqlPorts()
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.SqlPorts.ToList()
                    .Select(MapToSqlPortApiModel).ToList();
            }
        }

        public string GetSqlPort(string targetInstance)
        {
            using (var context = _contextFactory.GetContext())
            {
                var sqlPort = context.SqlPorts.FirstOrDefault(sp => sp.Instance_Name == targetInstance);
                return sqlPort != null ? sqlPort.SQL_Port : string.Empty;
            }
        }

        public void CreateSqlPort(SqlPortApiModel value)
        {
            using (var context = _contextFactory.GetContext())
            {
                var currentSqlPort = context.SqlPorts.FirstOrDefault(
                    sqlPort => sqlPort.Instance_Name == value.InstanceName
                    && sqlPort.SQL_Port == value.SqlPort);
                if (currentSqlPort != null)
                {
                    throw new ArgumentException($"Pair {value.InstanceName} - {value.SqlPort} already exist");
                }

                context.SqlPorts.Add(MapToSqlPort(value));
                context.SaveChanges();
            }
        }

        private SqlPortApiModel MapToSqlPortApiModel(SqlPort sql)
        {
            return new SqlPortApiModel
            {
                InstanceName = sql.Instance_Name,
                SqlPort = sql.SQL_Port
            };
        }

        private SqlPort MapToSqlPort(SqlPortApiModel sql)
        {
            return new SqlPort
            {
                Instance_Name = sql.InstanceName,
                SQL_Port = sql.SqlPort
            };
        }
    }
}