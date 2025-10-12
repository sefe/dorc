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
            using (var context = _contextFactory.GetContext())
            {
                var deployments = context.DeploymentRequests
                    .Where(dr => !string.IsNullOrEmpty(dr.Environment))
                    .GroupBy(dr => dr.Environment)
                    .Select(g => new AnalyticsEnvironmentUsageApiModel
                    {
                        EnvironmentName = g.Key,
                        CountOfDeployments = g.Count(),
                        Failed = g.Count(dr => dr.Status == "Failed" || dr.Status == "Failure")
                    })
                    .OrderByDescending(e => e.CountOfDeployments)
                    .ToList();

                return deployments;
            }
        }

        public IEnumerable<AnalyticsUserActivityApiModel> GetUserActivity()
        {
            using (var context = _contextFactory.GetContext())
            {
                var users = context.DeploymentRequests
                    .Where(dr => !string.IsNullOrEmpty(dr.UserName))
                    .GroupBy(dr => dr.UserName)
                    .Select(g => new AnalyticsUserActivityApiModel
                    {
                        UserName = g.Key,
                        CountOfDeployments = g.Count(),
                        Failed = g.Count(dr => dr.Status == "Failed" || dr.Status == "Failure")
                    })
                    .OrderByDescending(u => u.CountOfDeployments)
                    .ToList();

                return users;
            }
        }

        public IEnumerable<AnalyticsTimePatternApiModel> GetTimePatterns()
        {
            using (var context = _contextFactory.GetContext())
            {
                var patterns = context.DeploymentRequests
                    .Where(dr => dr.RequestedTime != null)
                    .AsEnumerable()
                    .GroupBy(dr => new { 
                        HourOfDay = dr.RequestedTime.Value.Hour,
                        DayOfWeek = (int)dr.RequestedTime.Value.DayOfWeek
                    })
                    .Select(g => new AnalyticsTimePatternApiModel
                    {
                        HourOfDay = g.Key.HourOfDay,
                        DayOfWeek = g.Key.DayOfWeek,
                        DayOfWeekName = ((DayOfWeek)g.Key.DayOfWeek).ToString(),
                        CountOfDeployments = g.Count()
                    })
                    .OrderByDescending(p => p.CountOfDeployments)
                    .ToList();

                return patterns;
            }
        }

        public IEnumerable<AnalyticsComponentUsageApiModel> GetComponentUsage()
        {
            using (var context = _contextFactory.GetContext())
            {
                var components = context.DeploymentRequests
                    .Where(dr => !string.IsNullOrEmpty(dr.Components))
                    .AsEnumerable()
                    .SelectMany(dr => dr.Components.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(c => c.Trim()))
                    .Where(c => !string.IsNullOrEmpty(c))
                    .GroupBy(c => c)
                    .Select(g => new AnalyticsComponentUsageApiModel
                    {
                        ComponentName = g.Key,
                        CountOfDeployments = g.Count()
                    })
                    .OrderByDescending(c => c.CountOfDeployments)
                    .Take(50)
                    .ToList();

                return components;
            }
        }

        public AnalyticsDurationApiModel GetDeploymentDuration()
        {
            using (var context = _contextFactory.GetContext())
            {
                var completedDeployments = context.DeploymentRequests
                    .Where(dr => dr.StartedTime != null && dr.CompletedTime != null)
                    .AsEnumerable()
                    .Select(dr => (dr.CompletedTime.Value - dr.StartedTime.Value).TotalMinutes)
                    .Where(duration => duration > 0 && duration < 1440) // Filter outliers (0-24 hours)
                    .ToList();

                if (!completedDeployments.Any())
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
                    AverageDurationMinutes = Math.Round(completedDeployments.Average(), 2),
                    MaxDurationMinutes = Math.Round(completedDeployments.Max(), 2),
                    MinDurationMinutes = Math.Round(completedDeployments.Min(), 2),
                    TotalDeployments = completedDeployments.Count
                };
            }
        }
    }
}