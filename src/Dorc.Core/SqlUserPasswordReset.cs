using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace Dorc.Core
{
    public class SqlUserPasswordReset : ISqlUserPasswordReset
    {
        // Allow-list for SQL login names accepted by the reset operation.
        // The single-quote is deliberately excluded: the reset sets the login
        // password equal to the login name (a documented "reset to default"
        // behaviour surfaced in the UI), so the name is embedded in an
        // N'...' string literal as well as a [...] quoted identifier. Excluding
        // the quote (and square brackets, which are not part of the allow-list)
        // removes any way to break out of either context.
        private const string UserSearchCriteriaRegExPattern = @"^[a-zA-Z0-9\-_. ()&]+$";
        private readonly ILogger _logger;

        public SqlUserPasswordReset(ILogger<SqlUserPasswordReset> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Returns true when the supplied login name contains only characters
        /// that are safe to embed in a quoted identifier and string literal.
        /// </summary>
        public static bool IsValidLoginName(string username)
        {
            return !string.IsNullOrEmpty(username)
                   && Regex.IsMatch(username, UserSearchCriteriaRegExPattern);
        }

        /// <summary>
        /// Builds the ALTER LOGIN statement that resets the login's password to
        /// the login name itself. The login name must already have passed
        /// <see cref="IsValidLoginName"/>. The identifier and the string literal
        /// are additionally escaped as defence-in-depth.
        /// </summary>
        public static string BuildResetLoginSql(string username)
        {
            // Escape the quoted identifier (double any ]) and the string literal
            // (double any '). The validated allow-list already excludes both, so
            // these are belt-and-suspenders.
            var escapedIdentifier = username.Replace("]", "]]");
            var escapedLiteral = username.Replace("'", "''");
            return "ALTER LOGIN [" + escapedIdentifier + "] WITH PASSWORD = N'" + escapedLiteral + "'";
        }

        public ApiBoolResult ResetSqlUserPassword(string targetDbServer, string username)
        {
            if (!IsValidLoginName(username))
            {
                var invalidMsg = $"Parameter username contains invalid value {username}";
                _logger.LogError(invalidMsg);
                return new ApiBoolResult { Result = false, Message = invalidMsg };
            }

            // Build the connection string via the builder rather than string
            // concatenation so that targetDbServer cannot inject additional
            // connection-string keywords.
            var connectionStringBuilder = new SqlConnectionStringBuilder
            {
                DataSource = targetDbServer,
                InitialCatalog = "master",
                IntegratedSecurity = true,
                TrustServerCertificate = true
            };

            using var objConn = new SqlConnection(connectionStringBuilder.ConnectionString);
            try
            {
                objConn.Open();

                var sql = BuildResetLoginSql(username);

                using var objCmd = new SqlCommand(sql, objConn);
                objCmd.ExecuteScalar();
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
