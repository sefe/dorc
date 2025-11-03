using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using log4net;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace Dorc.Core
{
    public class SqlUserPasswordReset : ISqlUserPasswordReset
    {
        private const string UserSearchCriteriaRegExPattern = @"^[a-zA-Z0-9-_.' ()&]+$";
        private readonly ILog _logger;

        public SqlUserPasswordReset(ILog logger)
        {
            _logger = logger;
        }

        public ApiBoolResult ResetSqlUserPassword(string targetDbServer, string username)
        {
            if (!Regex.IsMatch(username, UserSearchCriteriaRegExPattern))
            {
                _logger.Warn($"Invalid username format attempted: {username}");
                throw new ArgumentException("Parameter username contains invalid characters");
            }

            using (var objConn = new SqlConnection
            {
                ConnectionString = "Data Source=" + targetDbServer +
                                   ";Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True"
            })
            {
                try
                {
                    objConn.Open();

                    // Note: SQL Server doesn't support parameterizing object names (LOGIN names)
                    // Input is validated with regex above to prevent SQL injection
                    var sql = "ALTER LOGIN [" + username + "] WITH PASSWORD = N'" + username + "'";

                    using (var objCmd = new SqlCommand(sql, objConn))
                    {
                        var returnVal = objCmd.ExecuteScalar();
                        return new ApiBoolResult { Result = true };
                    }
                }
                catch (Exception e)
                {
                    var msg = $"Wasn't able to reset password for {username} on {targetDbServer}";
                    _logger.Error(msg, e);
                    return new ApiBoolResult { Result = false, Message = "Failed to reset SQL user password" + "\n" + e.Message };
                }
            }
        }
    }
}