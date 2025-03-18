using System;
using Dapper;
using System.Data.SqlClient;
using Serilog;

namespace Dorc.PersistData.Dapper
{
    public class DapperContext : IDapperContext
    {
        private string connectionString;

        public DapperContext( string connectionString)
        {
            this.connectionString = connectionString;
        }

        public void UpdateLog(ILogger contextLogger, int deploymentResultId, string log)
        {
            var sql = "";
            try
            {
                sql = $@"UPDATE [deploy].[DeploymentResult]
                             SEt log= ISNULL(log,'') +CHAR(10)+ '{log.Replace("'", "''")}'
                             where id = {deploymentResultId}";

                using (var connection = new SqlConnection(connectionString))
                {
                    var affectedRows = connection.Execute(sql);
                    contextLogger.Verbose(" sql {0} - Updated {1} rows ",sql, affectedRows);
                }
            }
            catch (Exception e)
            {
                contextLogger.Information($"sql {sql} encountered error {e.Message}");
            }
        }

        public void AddLogFilePath(ILogger contextLogger,int deploymentRequestId, string logFilePath)
        {
            var sql = $@"UPDATE [deploy].[DeploymentRequest]
                         SET unclogpath='{logFilePath}'
                         where id={deploymentRequestId}";
            using (var connection = new SqlConnection(connectionString))
            {
                var affectedRows = connection.Execute(sql);
                contextLogger.Verbose(" sql {0} - Updated {1} rows ", sql, affectedRows);

            }
        }


    }
}
