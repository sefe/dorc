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
                throw new ArgumentException($"Parameter username contains invalid value {username}");

            using (var objConn = new SqlConnection
            {
                ConnectionString = "Data Source=" + targetDbServer +
                                   ";Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True"
            })
            {
                try
                {
                    objConn.Open();

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
                    return new ApiBoolResult { Result = false, Message = msg + "\n" + e.Message };
                }
            }
        }
    }
}