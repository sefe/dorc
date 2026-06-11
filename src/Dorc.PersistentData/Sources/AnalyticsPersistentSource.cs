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

        public AnalyticsDeploymentSummaryApiModel GetDeploymentSummary()
        {
            using (var context = _contextFactory.GetContext())
            {
                var rows = context.AnalyticsDeploymentsByProjectDate
                    .Select(row => new
                    {
                        row.ProjectName,
                        row.Year,
                        row.CountOfDeployments,
                        row.Failed
                    })
                    .ToList();

                var currentYear = DateTime.Today.Year;
                var thisYear = rows.Where(row => row.Year == currentYear).ToList();

                var summary = new AnalyticsDeploymentSummaryApiModel
                {
                    TotalDeployments = rows.Sum(row => row.CountOfDeployments),
                    TotalDeploymentsThisYear = thisYear.Sum(row => row.CountOfDeployments),
                    TotalFailedDeploymentsThisYear = thisYear.Sum(row => row.Failed),
                    BusiestDeploymentCount = thisYear.Count == 0
                        ? 0
                        : thisYear.Max(row => row.CountOfDeployments)
                };

                summary.PercentFailedThisYear = Percentage(
                    summary.TotalFailedDeploymentsThisYear, summary.TotalDeploymentsThisYear);

                // Days elapsed in the current year, inclusive of today and never zero,
                // so the average is well defined even on 1 January.
                var daysElapsed = (DateTime.Today - new DateTime(currentYear, 1, 1)).Days + 1;
                summary.AverageDeploymentsPerDay = (int)Math.Round(
                    (double)summary.TotalDeploymentsThisYear / Math.Max(daysElapsed, 1));

                var projectTotals = thisYear
                    .GroupBy(row => row.ProjectName ?? string.Empty)
                    .Select(group => new AnalyticsProjectDeploymentApiModel
                    {
                        ProjectName = group.Key,
                        CountOfDeployments = group.Sum(row => row.CountOfDeployments)
                    })
                    .OrderByDescending(project => project.CountOfDeployments)
                    .ToList();

                summary.TopProjectsThisYear = projectTotals.Take(3).ToList();
                summary.PercentTop3Projects = Percentage(
                    summary.TopProjectsThisYear.Sum(project => project.CountOfDeployments),
                    summary.TotalDeploymentsThisYear);

                return summary;
            }
        }

        private static int Percentage(int part, int whole)
        {
            return whole > 0 ? (int)Math.Round((double)part / whole * 100) : 0;
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

        private static readonly string[] DayNames =
            { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

        public IEnumerable<AnalyticsTimePatternApiModel> GetTimePatterns()
        {
            var output = new List<AnalyticsTimePatternApiModel>();
            using (var context = _contextFactory.GetContext())
            {
                var patterns = context.AnalyticsTimePattern
                    .OrderBy(pattern => pattern.HourOfDay)
                    .ThenBy(pattern => pattern.DayOfWeek)
                    .ToList();

                foreach (var pattern in patterns)
                {
                    // The population proc stores SQL Server WEEKDAY (1-7); convert to
                    // a 0-6 index. Skip rows outside the valid range rather than risk
                    // an IndexOutOfRangeException on unexpected data.
                    if (pattern.DayOfWeek < 1 || pattern.DayOfWeek > 7)
                        continue;

                    var dayIndex = pattern.DayOfWeek - 1;
                    output.Add(new AnalyticsTimePatternApiModel
                    {
                        HourOfDay = pattern.HourOfDay,
                        DayOfWeek = dayIndex,
                        DayOfWeekName = DayNames[dayIndex],
                        CountOfDeployments = pattern.DeploymentCount
                    });
                }
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
                        MinDurationMinutes = 0
                    };
                }

                return new AnalyticsDurationApiModel
                {
                    AverageDurationMinutes = (double)duration.AverageDurationMinutes,
                    MaxDurationMinutes = (double)duration.LongestDurationMinutes,
                    MinDurationMinutes = (double)duration.ShortestDurationMinutes
                };
            }
        }
    }
}