using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
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
                var rows = context.AnalyticsDeploymentsByProjectDate.ToList();
                return BuildSummary(rows, DateTime.Today);
            }
        }

        /// <summary>
        /// Pure aggregation of the per-project-per-date rows into the dashboard
        /// summary. Takes <paramref name="today"/> explicitly so the time-dependent
        /// arithmetic (this-year filter, average per day) is deterministically testable.
        /// </summary>
        public static AnalyticsDeploymentSummaryApiModel BuildSummary(
            IReadOnlyCollection<DeploymentsByProjectDate> rows, DateTime today)
        {
            var currentYear = today.Year;
            var thisYear = rows.Where(row => row.Year == currentYear).ToList();

            var summary = new AnalyticsDeploymentSummaryApiModel
            {
                TotalDeployments = rows.Sum(row => row.CountOfDeployments),
                TotalDeploymentsThisYear = thisYear.Sum(row => row.CountOfDeployments),
                TotalFailedDeploymentsThisYear = thisYear.Sum(row => row.Failed)
            };

            // Busiest single calendar day this year, summed across all projects.
            summary.BusiestDeploymentCount = thisYear
                .GroupBy(row => new { row.Year, row.Month, row.Day })
                .Select(group => group.Sum(row => row.CountOfDeployments))
                .DefaultIfEmpty(0)
                .Max();

            summary.PercentFailedThisYear = Percentage(
                summary.TotalFailedDeploymentsThisYear, summary.TotalDeploymentsThisYear);

            // Days elapsed in the current year, inclusive of today and never zero,
            // so the average is well defined even on 1 January.
            var daysElapsed = (today.Date - new DateTime(currentYear, 1, 1)).Days + 1;
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
                            Failed = env.FailCount,
                            LastSuccessfulDeployment = env.LastSuccessfulDeployment
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

                // The population proc stores SQL Server WEEKDAY (1-7); convert to
                // a 0-6 index. Filter out rows outside the valid range rather than
                // risk an IndexOutOfRangeException on unexpected data.
                foreach (var pattern in patterns.Where(pattern =>
                             pattern.DayOfWeek >= 1 && pattern.DayOfWeek <= 7))
                {
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
                // Bound the payload server-side: the UI only ever shows the top 15,
                // so returning the highest-count components with headroom keeps the
                // response small without relying on the population proc to truncate.
                output.AddRange(context.AnalyticsComponentUsage
                    .OrderByDescending(component => component.DeploymentCount)
                    .Take(50)
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
                    MinDurationMinutes = (double)duration.ShortestDurationMinutes,
                    P50DurationMinutes = (double?)duration.P50DurationMinutes,
                    P90DurationMinutes = (double?)duration.P90DurationMinutes,
                    P95DurationMinutes = (double?)duration.P95DurationMinutes
                };
            }
        }

        public IEnumerable<AnalyticsMonthlyOutcomeApiModel> GetMonthlyOutcomes()
        {
            var output = new List<AnalyticsMonthlyOutcomeApiModel>();
            using (var context = _contextFactory.GetContext())
            {
                output.AddRange(context.AnalyticsMonthlyOutcome
                    .OrderBy(outcome => outcome.Year)
                    .ThenBy(outcome => outcome.Month)
                    .ThenBy(outcome => outcome.IsProd)
                    .Select(outcome =>
                        new AnalyticsMonthlyOutcomeApiModel
                        {
                            Year = outcome.Year,
                            Month = outcome.Month,
                            IsProd = outcome.IsProd,
                            CountOfDeployments = outcome.CountOfDeployments,
                            Failed = outcome.Failed,
                            Cancelled = outcome.Cancelled
                        }));
            }
            return output;
        }

        public IEnumerable<AnalyticsEnvironmentWaitApiModel> GetEnvironmentWaitTimes()
        {
            var output = new List<AnalyticsEnvironmentWaitApiModel>();
            using (var context = _contextFactory.GetContext())
            {
                // Most-contended environments first; bounded for the dashboard.
                output.AddRange(context.AnalyticsEnvironmentWait
                    .OrderByDescending(wait => wait.MedianWaitMinutes)
                    .Take(50)
                    .Select(wait =>
                        new AnalyticsEnvironmentWaitApiModel
                        {
                            EnvironmentName = wait.EnvironmentName,
                            AvgWaitMinutes = (double)wait.AvgWaitMinutes,
                            MedianWaitMinutes = (double)wait.MedianWaitMinutes,
                            P90WaitMinutes = (double)wait.P90WaitMinutes,
                            SampleCount = wait.SampleCount
                        }));
            }
            return output;
        }

        public IEnumerable<AnalyticsProjectDurationApiModel> GetProjectDurations()
        {
            var output = new List<AnalyticsProjectDurationApiModel>();
            using (var context = _contextFactory.GetContext())
            {
                // Highest-volume projects first; bounded for the dashboard.
                output.AddRange(context.AnalyticsProjectDuration
                    .OrderByDescending(duration => duration.SampleCount)
                    .Take(50)
                    .Select(duration =>
                        new AnalyticsProjectDurationApiModel
                        {
                            ProjectName = duration.ProjectName,
                            MedianDurationMinutes = (double)duration.MedianDurationMinutes,
                            P90DurationMinutes = (double)duration.P90DurationMinutes,
                            SampleCount = duration.SampleCount
                        }));
            }
            return output;
        }

        public IEnumerable<AnalyticsComponentReliabilityApiModel> GetComponentReliability()
        {
            var output = new List<AnalyticsComponentReliabilityApiModel>();
            using (var context = _contextFactory.GetContext())
            {
                // Most failures first; bounded for the dashboard.
                output.AddRange(context.AnalyticsComponentReliability
                    .OrderByDescending(component => component.FailedCount)
                    .Take(50)
                    .Select(component =>
                        new AnalyticsComponentReliabilityApiModel
                        {
                            ComponentName = component.ComponentName,
                            AttemptCount = component.AttemptCount,
                            FailedCount = component.FailedCount,
                            RetryAttemptCount = component.RetryAttemptCount
                        }));
            }
            return output;
        }

        public IEnumerable<AnalyticsRecoveryTimeApiModel> GetRecoveryTimes()
        {
            var output = new List<AnalyticsRecoveryTimeApiModel>();
            using (var context = _contextFactory.GetContext())
            {
                // Slowest-to-recover projects first; bounded for the dashboard.
                output.AddRange(context.AnalyticsRecoveryTime
                    .OrderByDescending(recovery => recovery.MedianRecoveryHours)
                    .Take(50)
                    .Select(recovery =>
                        new AnalyticsRecoveryTimeApiModel
                        {
                            ProjectName = recovery.ProjectName,
                            MedianRecoveryHours = (double)recovery.MedianRecoveryHours,
                            AvgRecoveryHours = (double)recovery.AvgRecoveryHours,
                            SampleCount = recovery.SampleCount
                        }));
            }
            return output;
        }
    }
}