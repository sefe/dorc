using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace Dorc.Core
{
    public class SqlUserPasswordReset : ISqlUserPasswordReset
    {
        private const string UserSearchCriteriaRegExPattern = @"^[a-zA-Z0-9-_.' ()&]+$";
        private readonly ILogger<SqlUserPasswordReset> _logger;

        public SqlUserPasswordReset(ILogger<SqlUserPasswordReset> logger)
        {
            _logger = logger;
        }

        public ApiBoolResult ResetSqlUserPassword(string targetDbServer, string username)
        {
            var objConn = new SqlConnection
            {
                ConnectionString = "Data Source=" + targetDbServer +
                                   ";Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True"
            };
            try
            {
                if (!Regex.IsMatch(username, UserSearchCriteriaRegExPattern))
                    throw new ArgumentException($"Parameter username contains invalid value {username}");

                objConn.Open();

                var sql = "ALTER LOGIN [" + username + "] WITH PASSWORD = N'" + username + "'";

                var objCmd = new SqlCommand(sql, objConn);
                var returnVal = objCmd.ExecuteScalar();
                return new ApiBoolResult { Result = true };
            }
            catch (Exception e)
            {
                var msg = $"Wasn't able to reset password for {username} on {targetDbServer}";
                _logger.LogError(e, msg);
                return new ApiBoolResult { Result = false, Message = msg + "\n" + e.Message };
            }
            finally
            {
                objConn.Close();

            }
        }
    }
}