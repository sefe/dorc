using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.RegularExpressions;

namespace Dorc.Core
{
    public class SqlUserPasswordReset : ISqlUserPasswordReset
    {
        private const string ServerNamePattern = @"^[a-zA-Z0-9._\-]+(\\[a-zA-Z0-9_$#\-]+)?(,[0-9]{1,5})?$";
        private const string SqlLoginIdentifierPattern = @"^[a-zA-Z0-9_][a-zA-Z0-9_.\-]{0,127}$";
        internal const string AlterLoginParameterizedSql =
            "DECLARE @stmt nvarchar(600) = N'ALTER LOGIN ' + QUOTENAME(@loginName, '[') + N' WITH PASSWORD = N' + QUOTENAME(@newPassword, ''''); IF @stmt IS NULL THROW 50000, 'Invalid SQL login parameter.', 1; EXEC sys.sp_executesql @stmt;";
        private readonly ILogger _logger;

        public SqlUserPasswordReset(ILogger<SqlUserPasswordReset> logger)
        {
            _logger = logger;
        }

        public ApiBoolResult ResetSqlUserPassword(string targetDbServer, string username)
        {
            try
            {
                string connectionString = BuildConnectionString(targetDbServer);
                ValidateSqlLoginIdentifier(username);

                using var objConn = new SqlConnection(connectionString);
                using var objCmd = CreateAlterLoginCommand(objConn, username);
                objConn.Open();

                objCmd.ExecuteNonQuery();
                return new ApiBoolResult { Result = true };
            }
            catch (Exception e)
            {
                string safeUsername = (username ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
                string safeTargetDbServer = (targetDbServer ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
                var msg = $"Wasn't able to reset password for {safeUsername} on {safeTargetDbServer}";
                _logger.LogError(
                    e,
                    "Wasn't able to reset password for {Username} on {TargetDbServer}",
                    safeUsername,
                    safeTargetDbServer);
                return new ApiBoolResult { Result = false, Message = msg + "\n" + e.Message };
            }
        }

        internal static SqlCommand CreateAlterLoginCommand(SqlConnection connection, string username)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ValidateSqlLoginIdentifier(username);

            var command = new SqlCommand(AlterLoginParameterizedSql, connection);
            command.Parameters.Add(new SqlParameter("@loginName", SqlDbType.NVarChar, 128) { Value = username });
            command.Parameters.Add(new SqlParameter("@newPassword", SqlDbType.NVarChar, 128) { Value = username });
            return command;
        }

        internal static string BuildConnectionString(string targetDbServer)
        {
            if (string.IsNullOrWhiteSpace(targetDbServer)
                || targetDbServer.Any(ch => ch == ';' || char.IsWhiteSpace(ch) || char.IsControl(ch) || ch == '\'' || ch == '"')
                || !Regex.IsMatch(targetDbServer, ServerNamePattern))
            {
                throw new ArgumentException("Target database server name is invalid.", nameof(targetDbServer));
            }

            return new SqlConnectionStringBuilder
            {
                DataSource = targetDbServer,
                InitialCatalog = "master",
                IntegratedSecurity = true,
                TrustServerCertificate = true
            }.ConnectionString;
        }

        internal static void ValidateSqlLoginIdentifier(string username)
        {
            if (string.IsNullOrWhiteSpace(username)
                || username.Any(ch => char.IsWhiteSpace(ch) || char.IsControl(ch))
                || !Regex.IsMatch(username, SqlLoginIdentifierPattern))
            {
                throw new ArgumentException("SQL login name contains invalid characters.", nameof(username));
            }
        }

        internal static string BuildAlterLoginStatement(string username)
        {
            ValidateSqlLoginIdentifier(username);

            using var builder = new SqlCommandBuilder();
            string quotedIdentifier = builder.QuoteIdentifier(username);
            string quotedPasswordLiteral = QuoteSqlUnicodeLiteral(username);

            return $"ALTER LOGIN {quotedIdentifier} WITH PASSWORD = {quotedPasswordLiteral}";
        }

        internal static string QuoteSqlUnicodeLiteral(string value)
        {
            return "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        }
    }
}