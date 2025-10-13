using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{
    public class AnalyticsPersistentSource : IAnalyticsPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public AnalyticsPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        private List<T> ExecuteRawSql<T>(IDeploymentContext context, string sql, Func<SqlDataReader, T> mapper)
        {
            var results = new List<T>();
            var connection = context.Database.GetDbConnection();
            var connectionWasOpen = connection.State == System.Data.ConnectionState.Open;
            
            try
            {
                if (!connectionWasOpen)
                    connection.Open();
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    using (var reader = (SqlDataReader)command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(mapper(reader));
                        }
                    }
                }
            }
            finally
            {
                if (!connectionWasOpen)
                    connection.Close();
            }

            return results;
        }

        public IEnumerable<AnalyticsDeploymentsPerProjectApiModel> GetCountDeploymentsPerProjectMonth()
        {
            var output = new List<AnalyticsDeploymentsPerProjectApiModel>();
            using (var context = _contextFactory.GetContext())
            {
                var spSelectDeploymentsByProject =
                    context.AnalyticsDeploymentsByProjectMonth;

                output.AddRange(spSelectDeploymentsByProject.Select(projectResultDbo =>
                    new AnalyticsDeploymentsPerProjectApiModel
                    {
                        CountOfDeployments = projectResultDbo.CountOfDeployments,
                        Year = projectResultDbo.Year,
                        Month = projectResultDbo.Month,
                        ProjectName = projectResultDbo.ProjectName,
                        Failed = projectResultDbo.Failed
                    }));
            }

            return output;
        }

        public IEnumerable<AnalyticsDeploymentsPerProjectApiModel> GetCountDeploymentsPerProjectDate()
        {
            var output = new List<AnalyticsDeploymentsPerProjectApiModel>();

            using (var context = _contextFactory.GetContext())
            {
                var spSelectDeploymentsByProject =
                    context.AnalyticsDeploymentsByProjectDate;

                output.AddRange(spSelectDeploymentsByProject.Select(projectResultDbo =>
                    new AnalyticsDeploymentsPerProjectApiModel
                    {
                        CountOfDeployments = projectResultDbo.CountOfDeployments,
                        Year = projectResultDbo.Year,
                        Month = projectResultDbo.Month,
                        Day = projectResultDbo.Day,
                        ProjectName = projectResultDbo.ProjectName,
                        Failed = projectResultDbo.Failed
                    }));
            }
            return output;
        }

        public IEnumerable<AnalyticsEnvironmentUsageApiModel> GetEnvironmentUsage()
        {
            using (var context = _contextFactory.GetContext())
            {
                // Use raw SQL to efficiently query both main and archive tables
                var sql = @"
                    SELECT 
                        Environment AS EnvironmentName,
                        COUNT(*) AS CountOfDeployments,
                        SUM(CASE WHEN Status IN ('Failed', 'Failure') THEN 1 ELSE 0 END) AS Failed
                    FROM (
                        SELECT Environment, Status FROM dbo.DeploymentRequest WHERE Environment IS NOT NULL AND Environment != ''
                        UNION ALL
                        SELECT Environment, Status FROM archive.DeploymentRequest WHERE Environment IS NOT NULL AND Environment != ''
                    ) AS AllDeployments
                    GROUP BY Environment
                    ORDER BY CountOfDeployments DESC";

                return ExecuteRawSql(context, sql, reader => new AnalyticsEnvironmentUsageApiModel
                {
                    EnvironmentName = reader.GetString(0),
                    CountOfDeployments = reader.GetInt32(1),
                    Failed = reader.GetInt32(2)
                });
            }
        }

        public IEnumerable<AnalyticsUserActivityApiModel> GetUserActivity()
        {
            using (var context = _contextFactory.GetContext())
            {
                // Use raw SQL to efficiently query both main and archive tables
                var sql = @"
                    SELECT 
                        UserName,
                        COUNT(*) AS CountOfDeployments,
                        SUM(CASE WHEN Status IN ('Failed', 'Failure') THEN 1 ELSE 0 END) AS Failed
                    FROM (
                        SELECT UserName, Status FROM dbo.DeploymentRequest WHERE UserName IS NOT NULL AND UserName != ''
                        UNION ALL
                        SELECT UserName, Status FROM archive.DeploymentRequest WHERE UserName IS NOT NULL AND UserName != ''
                    ) AS AllDeployments
                    GROUP BY UserName
                    ORDER BY CountOfDeployments DESC";

                return ExecuteRawSql(context, sql, reader => new AnalyticsUserActivityApiModel
                {
                    UserName = reader.GetString(0),
                    CountOfDeployments = reader.GetInt32(1),
                    Failed = reader.GetInt32(2)
                });
            }
        }

        public IEnumerable<AnalyticsTimePatternApiModel> GetTimePatterns()
        {
            using (var context = _contextFactory.GetContext())
            {
                // Use raw SQL with aggregation for performance
                var sql = @"
                    SELECT 
                        DATEPART(HOUR, RequestedTime) AS HourOfDay,
                        DATEPART(WEEKDAY, RequestedTime) - 1 AS DayOfWeek,
                        DATENAME(WEEKDAY, RequestedTime) AS DayOfWeekName,
                        COUNT(*) AS CountOfDeployments
                    FROM (
                        SELECT RequestedTime FROM dbo.DeploymentRequest WHERE RequestedTime IS NOT NULL
                        UNION ALL
                        SELECT RequestedTime FROM archive.DeploymentRequest WHERE RequestedTime IS NOT NULL
                    ) AS AllDeployments
                    GROUP BY DATEPART(HOUR, RequestedTime), DATEPART(WEEKDAY, RequestedTime), DATENAME(WEEKDAY, RequestedTime)
                    ORDER BY CountOfDeployments DESC";

                return ExecuteRawSql(context, sql, reader => new AnalyticsTimePatternApiModel
                {
                    HourOfDay = reader.GetInt32(0),
                    DayOfWeek = reader.GetInt32(1),
                    DayOfWeekName = reader.GetString(2),
                    CountOfDeployments = reader.GetInt32(3)
                });
            }
        }

        public IEnumerable<AnalyticsComponentUsageApiModel> GetComponentUsage()
        {
            using (var context = _contextFactory.GetContext())
            {
                // Use raw SQL with STRING_SPLIT for performance (SQL Server 2016+)
                var sql = @"
                    SELECT TOP 50
                        LTRIM(RTRIM(value)) AS ComponentName,
                        COUNT(*) AS CountOfDeployments
                    FROM (
                        SELECT Components FROM dbo.DeploymentRequest WHERE Components IS NOT NULL AND Components != ''
                        UNION ALL
                        SELECT Components FROM archive.DeploymentRequest WHERE Components IS NOT NULL AND Components != ''
                    ) AS AllDeployments
                    CROSS APPLY STRING_SPLIT(Components, ',')
                    WHERE LTRIM(RTRIM(value)) != ''
                    GROUP BY LTRIM(RTRIM(value))
                    ORDER BY CountOfDeployments DESC";

                return ExecuteRawSql(context, sql, reader => new AnalyticsComponentUsageApiModel
                {
                    ComponentName = reader.GetString(0),
                    CountOfDeployments = reader.GetInt32(1)
                });
            }
        }

        public AnalyticsDurationApiModel GetDeploymentDuration()
        {
            using (var context = _contextFactory.GetContext())
            {
                // Use raw SQL for efficient aggregation
                var sql = @"
                    SELECT 
                        ISNULL(ROUND(AVG(CAST(DurationMinutes AS FLOAT)), 2), 0) AS AverageDurationMinutes,
                        ISNULL(ROUND(MAX(CAST(DurationMinutes AS FLOAT)), 2), 0) AS MaxDurationMinutes,
                        ISNULL(ROUND(MIN(CAST(DurationMinutes AS FLOAT)), 2), 0) AS MinDurationMinutes,
                        COUNT(*) AS TotalDeployments
                    FROM (
                        SELECT DATEDIFF(MINUTE, StartedTime, CompletedTime) AS DurationMinutes
                        FROM dbo.DeploymentRequest 
                        WHERE StartedTime IS NOT NULL AND CompletedTime IS NOT NULL
                        UNION ALL
                        SELECT DATEDIFF(MINUTE, StartedTime, CompletedTime) AS DurationMinutes
                        FROM archive.DeploymentRequest 
                        WHERE StartedTime IS NOT NULL AND CompletedTime IS NOT NULL
                    ) AS AllDurations
                    WHERE DurationMinutes > 0 AND DurationMinutes < 1440";

                var result = ExecuteRawSql(context, sql, reader => new AnalyticsDurationApiModel
                {
                    AverageDurationMinutes = reader.GetDouble(0),
                    MaxDurationMinutes = reader.GetDouble(1),
                    MinDurationMinutes = reader.GetDouble(2),
                    TotalDeployments = reader.GetInt32(3)
                }).FirstOrDefault();

                return result ?? new AnalyticsDurationApiModel
                {
                    AverageDurationMinutes = 0,
                    MaxDurationMinutes = 0,
                    MinDurationMinutes = 0,
                    TotalDeployments = 0
                };
            }
        }
    }
}