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
            if (value == null || string.IsNullOrWhiteSpace(value.InstanceName) || string.IsNullOrWhiteSpace(value.SqlPort))
            {
                throw new ArgumentException("InstanceName and SqlPort are required.");
            }

            if (value.InstanceName.Length > 250 || value.SqlPort.Length > 10)
            {
                throw new ArgumentException("InstanceName cannot exceed 250 characters and SqlPort cannot exceed 10 characters.");
            }

            if (!int.TryParse(value.SqlPort, out var portNumber) || portNumber < 1 || portNumber > 65535)
            {
                throw new ArgumentException("SqlPort must be a valid TCP port number between 1 and 65535.");
            }

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

        public void DeleteSqlPort(SqlPortApiModel value)
        {
            using (var context = _contextFactory.GetContext())
            {
                var currentSqlPort = context.SqlPorts.FirstOrDefault(
                    sqlPort => sqlPort.Instance_Name == value.InstanceName
                    && sqlPort.SQL_Port == value.SqlPort);
                if (currentSqlPort == null)
                {
                    throw new ArgumentException($"Pair {value.InstanceName} - {value.SqlPort} does not exist");
                }

                context.SqlPorts.Remove(currentSqlPort);
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