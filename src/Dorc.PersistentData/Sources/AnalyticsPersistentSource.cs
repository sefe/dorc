using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.PersistentData.Sources
{
    public class AnalyticsPersistentSource : IAnalyticsPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public AnalyticsPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
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
            var output = new List<AnalyticsEnvironmentUsageApiModel>();
            using (var context = _contextFactory.GetContext())
            {
                output.AddRange(context.AnalyticsEnvironmentUsage
                    .OrderByDescending(env => env.TotalDeployments)
                    .Select(env =>
                        new AnalyticsEnvironmentUsageApiModel
                        {
                            EnvironmentName = env.EnvironmentName,
                            CountOfDeployments = env.TotalDeployments,
                            Failed = env.FailCount
                        }));
            }
            return output;
        }

        public IEnumerable<AnalyticsUserActivityApiModel> GetUserActivity()
        {
            var output = new List<AnalyticsUserActivityApiModel>();
            using (var context = _contextFactory.GetContext())
            {
                output.AddRange(context.AnalyticsUserActivity
                    .OrderByDescending(user => user.TotalDeployments)
                    .Select(user =>
                        new AnalyticsUserActivityApiModel
                        {
                            UserName = user.UserName,
                            CountOfDeployments = user.TotalDeployments,
                            Failed = user.FailCount
                        }));
            }
            return output;
        }

        public IEnumerable<AnalyticsTimePatternApiModel> GetTimePatterns()
        {
            var output = new List<AnalyticsTimePatternApiModel>();
            using (var context = _contextFactory.GetContext())
            {
                var dayNames = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
                output.AddRange(context.AnalyticsTimePattern
                    .OrderBy(pattern => pattern.HourOfDay)
                    .ThenBy(pattern => pattern.DayOfWeek)
                    .Select(pattern =>
                        new AnalyticsTimePatternApiModel
                        {
                            HourOfDay = pattern.HourOfDay,
                            DayOfWeek = pattern.DayOfWeek - 1, // Convert SQL Server WEEKDAY (1-7) to 0-6
                            DayOfWeekName = dayNames[pattern.DayOfWeek - 1],
                            CountOfDeployments = pattern.DeploymentCount
                        }));
            }
            return output;
        }

        public IEnumerable<AnalyticsComponentUsageApiModel> GetComponentUsage()
        {
            var output = new List<AnalyticsComponentUsageApiModel>();
            using (var context = _contextFactory.GetContext())
            {
                output.AddRange(context.AnalyticsComponentUsage
                    .OrderByDescending(component => component.DeploymentCount)
                    .Select(component =>
                        new AnalyticsComponentUsageApiModel
                        {
                            ComponentName = component.ComponentName,
                            CountOfDeployments = component.DeploymentCount
                        }));
            }
            return output;
        }

        public AnalyticsDurationApiModel GetDeploymentDuration()
        {
            using (var context = _contextFactory.GetContext())
            {
                var duration = context.AnalyticsDuration.FirstOrDefault();
                
                if (duration == null)
                {
                    return new AnalyticsDurationApiModel
                    {
                        AverageDurationMinutes = 0,
                        MaxDurationMinutes = 0,
                        MinDurationMinutes = 0,
                        TotalDeployments = 0
                    };
                }

                return new AnalyticsDurationApiModel
                {
                    AverageDurationMinutes = (double)duration.AverageDurationMinutes,
                    MaxDurationMinutes = (double)duration.LongestDurationMinutes,
                    MinDurationMinutes = (double)duration.ShortestDurationMinutes,
                    TotalDeployments = 0 // Not stored in table, can be calculated if needed
                };
            }
        }
    }
}