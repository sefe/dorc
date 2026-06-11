using Dorc.Api.Tests.Mocks;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using NSubstitute;

namespace Dorc.Api.Tests.Sources
{
    /// <summary>
    /// Tests for AnalyticsPersistentSource covering the server-side deployment
    /// summary aggregation, the SQL-weekday to zero-based day-of-week mapping,
    /// and deployment duration mapping.
    /// </summary>
    [TestClass]
    public class AnalyticsPersistentSourceTests
    {
        private IDeploymentContextFactory _contextFactory;
        private IDeploymentContext _context;
        private AnalyticsPersistentSource _source;

        [TestInitialize]
        public void Setup()
        {
            _contextFactory = Substitute.For<IDeploymentContextFactory>();
            _context = Substitute.For<IDeploymentContext>();
            _contextFactory.GetContext().Returns(_context);
            _source = new AnalyticsPersistentSource(_contextFactory);
        }

        private void SetupDateRows(List<DeploymentsByProjectDate> rows)
        {
            _context.AnalyticsDeploymentsByProjectDate
                .Returns(DbContextMock.GetQueryableMockDbSet(rows));
        }

        private void SetupTimePatterns(List<AnalyticsTimePattern> patterns)
        {
            _context.AnalyticsTimePattern
                .Returns(DbContextMock.GetQueryableMockDbSet(patterns));
        }

        private void SetupDurations(List<AnalyticsDuration> durations)
        {
            _context.AnalyticsDuration
                .Returns(DbContextMock.GetQueryableMockDbSet(durations));
        }

        [TestMethod]
        public void GetDeploymentSummary_AggregatesTotalsAcrossAllYears()
        {
            var year = DateTime.Today.Year;
            SetupDateRows(new List<DeploymentsByProjectDate>
            {
                new() { ProjectName = "A", Year = year, CountOfDeployments = 10, Failed = 2 },
                new() { ProjectName = "A", Year = year, CountOfDeployments = 5, Failed = 1 },
                new() { ProjectName = "B", Year = year, CountOfDeployments = 8, Failed = 0 },
                new() { ProjectName = "C", Year = year, CountOfDeployments = 3, Failed = 3 },
                new() { ProjectName = "D", Year = year - 1, CountOfDeployments = 100, Failed = 50 }
            });

            var summary = _source.GetDeploymentSummary();

            Assert.AreEqual(126, summary.TotalDeployments);
            Assert.AreEqual(26, summary.TotalDeploymentsThisYear);
            Assert.AreEqual(6, summary.TotalFailedDeploymentsThisYear);
            Assert.AreEqual(10, summary.BusiestDeploymentCount);
        }

        [TestMethod]
        public void GetDeploymentSummary_ComputesPercentagesAndTopThreeProjects()
        {
            var year = DateTime.Today.Year;
            SetupDateRows(new List<DeploymentsByProjectDate>
            {
                new() { ProjectName = "A", Year = year, CountOfDeployments = 10, Failed = 2 },
                new() { ProjectName = "A", Year = year, CountOfDeployments = 5, Failed = 1 },
                new() { ProjectName = "B", Year = year, CountOfDeployments = 8, Failed = 0 },
                new() { ProjectName = "C", Year = year, CountOfDeployments = 3, Failed = 3 }
            });

            var summary = _source.GetDeploymentSummary();

            // 6 failures of 26 deployments = 23%
            Assert.AreEqual(23, summary.PercentFailedThisYear);
            // Top 3 projects total = all 26, so 100%
            Assert.AreEqual(100, summary.PercentTop3Projects);
            Assert.AreEqual(3, summary.TopProjectsThisYear.Count);
            Assert.AreEqual("A", summary.TopProjectsThisYear[0].ProjectName);
            Assert.AreEqual(15, summary.TopProjectsThisYear[0].CountOfDeployments);
        }

        [TestMethod]
        public void GetDeploymentSummary_EmptyData_ReturnsZerosWithoutDivideByZero()
        {
            SetupDateRows(new List<DeploymentsByProjectDate>());

            var summary = _source.GetDeploymentSummary();

            Assert.AreEqual(0, summary.TotalDeployments);
            Assert.AreEqual(0, summary.TotalDeploymentsThisYear);
            Assert.AreEqual(0, summary.PercentFailedThisYear);
            Assert.AreEqual(0, summary.PercentTop3Projects);
            Assert.AreEqual(0, summary.AverageDeploymentsPerDay);
            Assert.AreEqual(0, summary.BusiestDeploymentCount);
            Assert.AreEqual(0, summary.TopProjectsThisYear.Count);
        }

        [TestMethod]
        public void GetTimePatterns_MapsSqlWeekdayToZeroBasedIndexAndName()
        {
            SetupTimePatterns(new List<AnalyticsTimePattern>
            {
                new() { HourOfDay = 9, DayOfWeek = 1, DeploymentCount = 5 },
                new() { HourOfDay = 10, DayOfWeek = 7, DeploymentCount = 3 }
            });

            var result = _source.GetTimePatterns().ToList();

            var sunday = result.Single(p => p.HourOfDay == 9);
            Assert.AreEqual(0, sunday.DayOfWeek);
            Assert.AreEqual("Sunday", sunday.DayOfWeekName);

            var saturday = result.Single(p => p.HourOfDay == 10);
            Assert.AreEqual(6, saturday.DayOfWeek);
            Assert.AreEqual("Saturday", saturday.DayOfWeekName);
        }

        [TestMethod]
        public void GetTimePatterns_SkipsOutOfRangeDayOfWeekWithoutThrowing()
        {
            SetupTimePatterns(new List<AnalyticsTimePattern>
            {
                new() { HourOfDay = 9, DayOfWeek = 1, DeploymentCount = 5 },
                new() { HourOfDay = 11, DayOfWeek = 0, DeploymentCount = 99 },
                new() { HourOfDay = 12, DayOfWeek = 8, DeploymentCount = 99 }
            });

            var result = _source.GetTimePatterns().ToList();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(9, result[0].HourOfDay);
        }

        [TestMethod]
        public void GetDeploymentDuration_NoData_ReturnsZeros()
        {
            SetupDurations(new List<AnalyticsDuration>());

            var result = _source.GetDeploymentDuration();

            Assert.AreEqual(0, result.AverageDurationMinutes);
            Assert.AreEqual(0, result.MaxDurationMinutes);
            Assert.AreEqual(0, result.MinDurationMinutes);
        }

        [TestMethod]
        public void GetDeploymentDuration_MapsStoredValues()
        {
            SetupDurations(new List<AnalyticsDuration>
            {
                new()
                {
                    AverageDurationMinutes = 10.5m,
                    LongestDurationMinutes = 20m,
                    ShortestDurationMinutes = 2m
                }
            });

            var result = _source.GetDeploymentDuration();

            Assert.AreEqual(10.5, result.AverageDurationMinutes);
            Assert.AreEqual(20, result.MaxDurationMinutes);
            Assert.AreEqual(2, result.MinDurationMinutes);
        }
    }
}
