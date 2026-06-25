using Dorc.Api.Tests.Mocks;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Principal;

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
        private IEnvironmentsPersistentSource _environmentsPersistentSource;
        private AnalyticsPersistentSource _source;

        [TestInitialize]
        public void Setup()
        {
            _contextFactory = Substitute.For<IDeploymentContextFactory>();
            _context = Substitute.For<IDeploymentContext>();
            _contextFactory.GetContext().Returns(_context);
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _source = new AnalyticsPersistentSource(_contextFactory,
                _environmentsPersistentSource,
                Substitute.For<ILogger<AnalyticsPersistentSource>>());
        }

        private void SetupDateRows(List<DeploymentsByProjectDate> rows)
        {
            var dbSet = DbContextMock.GetQueryableMockDbSet(rows);
            _context.AnalyticsDeploymentsByProjectDate.Returns(dbSet);
        }

        private void SetupTimePatterns(List<AnalyticsTimePattern> patterns)
        {
            var dbSet = DbContextMock.GetQueryableMockDbSet(patterns);
            _context.AnalyticsTimePattern.Returns(dbSet);
        }

        private void SetupDurations(List<AnalyticsDuration> durations)
        {
            var dbSet = DbContextMock.GetQueryableMockDbSet(durations);
            _context.AnalyticsDuration.Returns(dbSet);
        }

        [TestMethod]
        public void GetDeploymentSummary_AggregatesTotalsAcrossAllYears()
        {
            var year = DateTime.Today.Year;
            SetupDateRows(new List<DeploymentsByProjectDate>
            {
                new() { ProjectName = "A", Year = year, Month = 1, Day = 1, CountOfDeployments = 10, Failed = 2 },
                new() { ProjectName = "A", Year = year, Month = 1, Day = 2, CountOfDeployments = 5, Failed = 1 },
                new() { ProjectName = "B", Year = year, Month = 1, Day = 1, CountOfDeployments = 8, Failed = 0 },
                new() { ProjectName = "C", Year = year, Month = 2, Day = 3, CountOfDeployments = 3, Failed = 3 },
                new() { ProjectName = "D", Year = year - 1, Month = 6, Day = 6, CountOfDeployments = 100, Failed = 50 }
            });

            var summary = _source.GetDeploymentSummary();

            Assert.AreEqual(126, summary.TotalDeployments);
            Assert.AreEqual(26, summary.TotalDeploymentsThisYear);
            Assert.AreEqual(6, summary.TotalFailedDeploymentsThisYear);
            // Busiest calendar day = Jan 1 (A:10 + B:8 = 18), summed across projects.
            Assert.AreEqual(18, summary.BusiestDeploymentCount);
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
        public void BuildSummary_AverageDeploymentsPerDay_DividesByDaysElapsedInclusive()
        {
            // 10 Jan -> 10 days elapsed (inclusive of 1 Jan and today).
            var today = new DateTime(2024, 1, 10);
            var rows = new List<DeploymentsByProjectDate>
            {
                new() { ProjectName = "A", Year = 2024, Month = 1, Day = 1, CountOfDeployments = 100 }
            };

            var summary = AnalyticsPersistentSource.BuildSummary(rows, today);

            Assert.AreEqual(10, summary.AverageDeploymentsPerDay);
        }

        [TestMethod]
        public void BuildSummary_OnFirstOfJanuary_DoesNotDivideByZero()
        {
            var today = new DateTime(2024, 1, 1);
            var rows = new List<DeploymentsByProjectDate>
            {
                new() { ProjectName = "A", Year = 2024, Month = 1, Day = 1, CountOfDeployments = 7 }
            };

            var summary = AnalyticsPersistentSource.BuildSummary(rows, today);

            Assert.AreEqual(7, summary.AverageDeploymentsPerDay);
        }

        [TestMethod]
        public void BuildSummary_BusiestDay_SumsAcrossProjectsNotPerProjectMax()
        {
            var today = new DateTime(2024, 6, 1);
            var rows = new List<DeploymentsByProjectDate>
            {
                // Same day, five projects of 4 each = 20.
                new() { ProjectName = "A", Year = 2024, Month = 1, Day = 1, CountOfDeployments = 4 },
                new() { ProjectName = "B", Year = 2024, Month = 1, Day = 1, CountOfDeployments = 4 },
                new() { ProjectName = "C", Year = 2024, Month = 1, Day = 1, CountOfDeployments = 4 },
                new() { ProjectName = "D", Year = 2024, Month = 1, Day = 1, CountOfDeployments = 4 },
                new() { ProjectName = "E", Year = 2024, Month = 1, Day = 1, CountOfDeployments = 4 },
                // A different day with a larger single-project count = 10.
                new() { ProjectName = "A", Year = 2024, Month = 1, Day = 2, CountOfDeployments = 10 }
            };

            var summary = AnalyticsPersistentSource.BuildSummary(rows, today);

            Assert.AreEqual(20, summary.BusiestDeploymentCount);
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
                    ShortestDurationMinutes = 2m,
                    P50DurationMinutes = 8m,
                    P90DurationMinutes = 18m,
                    P95DurationMinutes = 19.5m
                }
            });

            var result = _source.GetDeploymentDuration();

            Assert.AreEqual(10.5, result.AverageDurationMinutes);
            Assert.AreEqual(20, result.MaxDurationMinutes);
            Assert.AreEqual(2, result.MinDurationMinutes);
            Assert.AreEqual(8, result.P50DurationMinutes);
            Assert.AreEqual(18, result.P90DurationMinutes);
            Assert.AreEqual(19.5, result.P95DurationMinutes);
        }

        [TestMethod]
        public void GetDeploymentDuration_NullPercentiles_MapToNull()
        {
            SetupDurations(new List<AnalyticsDuration>
            {
                new()
                {
                    AverageDurationMinutes = 10m,
                    LongestDurationMinutes = 20m,
                    ShortestDurationMinutes = 2m
                }
            });

            var result = _source.GetDeploymentDuration();

            Assert.IsNull(result.P50DurationMinutes);
            Assert.IsNull(result.P90DurationMinutes);
            Assert.IsNull(result.P95DurationMinutes);
        }

        [TestMethod]
        public void GetEnvironmentUsage_MapsLastSuccessfulDeployment()
        {
            var user = Substitute.For<IPrincipal>();
            _environmentsPersistentSource.GetEnvironmentNames(user)
                .Returns(new[] { "ENV1", "ENV2" });

            var lastSuccess = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
            var usage = new List<AnalyticsEnvironmentUsage>
            {
                new() { EnvironmentName = "ENV1", TotalDeployments = 10, FailCount = 1, LastSuccessfulDeployment = lastSuccess },
                new() { EnvironmentName = "ENV2", TotalDeployments = 5, FailCount = 0, LastSuccessfulDeployment = null }
            };
            var dbSet = DbContextMock.GetQueryableMockDbSet(usage);
            _context.AnalyticsEnvironmentUsage.Returns(dbSet);

            var result = _source.GetEnvironmentUsage(user).ToList();

            Assert.AreEqual(lastSuccess, result.Single(e => e.EnvironmentName == "ENV1").LastSuccessfulDeployment);
            Assert.IsNull(result.Single(e => e.EnvironmentName == "ENV2").LastSuccessfulDeployment);
        }

        [TestMethod]
        public void GetEnvironmentUsage_ExcludesEnvironmentsNotAccessibleToUser()
        {
            var user = Substitute.For<IPrincipal>();
            // User can only see ENV1; ENV2 (no access) and Z_GMT_DELETED (not a live
            // environment) must be filtered out of the analytics snapshot.
            _environmentsPersistentSource.GetEnvironmentNames(user)
                .Returns(new[] { "ENV1" });

            var usage = new List<AnalyticsEnvironmentUsage>
            {
                new() { EnvironmentName = "ENV1", TotalDeployments = 10, FailCount = 1 },
                new() { EnvironmentName = "ENV2", TotalDeployments = 5, FailCount = 0 },
                new() { EnvironmentName = "Z_GMT_DELETED", TotalDeployments = 3, FailCount = 0 }
            };
            var dbSet = DbContextMock.GetQueryableMockDbSet(usage);
            _context.AnalyticsEnvironmentUsage.Returns(dbSet);

            var result = _source.GetEnvironmentUsage(user).ToList();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("ENV1", result.Single().EnvironmentName);
        }

        [TestMethod]
        public void GetEnvironmentUsage_MatchesEnvironmentNamesCaseInsensitively()
        {
            var user = Substitute.For<IPrincipal>();
            _environmentsPersistentSource.GetEnvironmentNames(user)
                .Returns(new[] { "env1" });

            var usage = new List<AnalyticsEnvironmentUsage>
            {
                new() { EnvironmentName = "ENV1", TotalDeployments = 10, FailCount = 1 }
            };
            var dbSet = DbContextMock.GetQueryableMockDbSet(usage);
            _context.AnalyticsEnvironmentUsage.Returns(dbSet);

            var result = _source.GetEnvironmentUsage(user).ToList();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("ENV1", result.Single().EnvironmentName);
        }

        [TestMethod]
        public void GetMonthlyOutcomes_OrdersByYearMonthAndMapsAllFields()
        {
            var outcomes = new List<AnalyticsMonthlyOutcome>
            {
                new() { Year = 2026, Month = 2, IsProd = false, CountOfDeployments = 30, Failed = 3, Cancelled = 1 },
                new() { Year = 2025, Month = 12, IsProd = true, CountOfDeployments = 10, Failed = 1, Cancelled = 0 }
            };
            var dbSet = DbContextMock.GetQueryableMockDbSet(outcomes);
            _context.AnalyticsMonthlyOutcome.Returns(dbSet);

            var result = _source.GetMonthlyOutcomes().ToList();

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(2025, result[0].Year);
            Assert.AreEqual(12, result[0].Month);
            Assert.IsTrue(result[0].IsProd);
            Assert.AreEqual(10, result[0].CountOfDeployments);
            Assert.AreEqual(1, result[0].Failed);
            Assert.AreEqual(0, result[0].Cancelled);
            Assert.AreEqual(2026, result[1].Year);
        }

        [TestMethod]
        public void GetEnvironmentWaitTimes_OrdersByMedianDescending()
        {
            var waits = new List<AnalyticsEnvironmentWait>
            {
                new() { EnvironmentName = "FAST", AvgWaitMinutes = 1m, MedianWaitMinutes = 1m, P90WaitMinutes = 2m, SampleCount = 100 },
                new() { EnvironmentName = "SLOW", AvgWaitMinutes = 40m, MedianWaitMinutes = 30m, P90WaitMinutes = 90m, SampleCount = 50 }
            };
            var dbSet = DbContextMock.GetQueryableMockDbSet(waits);
            _context.AnalyticsEnvironmentWait.Returns(dbSet);

            var result = _source.GetEnvironmentWaitTimes().ToList();

            Assert.AreEqual("SLOW", result[0].EnvironmentName);
            Assert.AreEqual(30, result[0].MedianWaitMinutes);
            Assert.AreEqual(90, result[0].P90WaitMinutes);
            Assert.AreEqual(50, result[0].SampleCount);
            Assert.AreEqual("FAST", result[1].EnvironmentName);
        }

        [TestMethod]
        public void GetProjectDurations_OrdersBySampleCountDescending()
        {
            var durations = new List<AnalyticsProjectDuration>
            {
                new() { ProjectName = "Small", MedianDurationMinutes = 5m, P90DurationMinutes = 9m, SampleCount = 10 },
                new() { ProjectName = "Big", MedianDurationMinutes = 4m, P90DurationMinutes = 12m, SampleCount = 1000 }
            };
            var dbSet = DbContextMock.GetQueryableMockDbSet(durations);
            _context.AnalyticsProjectDuration.Returns(dbSet);

            var result = _source.GetProjectDurations().ToList();

            Assert.AreEqual("Big", result[0].ProjectName);
            Assert.AreEqual(4, result[0].MedianDurationMinutes);
            Assert.AreEqual(12, result[0].P90DurationMinutes);
            Assert.AreEqual(1000, result[0].SampleCount);
        }

        [TestMethod]
        public void GetComponentReliability_OrdersByFailedCountDescending()
        {
            var components = new List<AnalyticsComponentReliability>
            {
                new() { ComponentName = "Stable", AttemptCount = 500, FailedCount = 1, RetryAttemptCount = 0 },
                new() { ComponentName = "Flaky", AttemptCount = 100, FailedCount = 25, RetryAttemptCount = 40 }
            };
            var dbSet = DbContextMock.GetQueryableMockDbSet(components);
            _context.AnalyticsComponentReliability.Returns(dbSet);

            var result = _source.GetComponentReliability().ToList();

            Assert.AreEqual("Flaky", result[0].ComponentName);
            Assert.AreEqual(100, result[0].AttemptCount);
            Assert.AreEqual(25, result[0].FailedCount);
            Assert.AreEqual(40, result[0].RetryAttemptCount);
        }

        [TestMethod]
        public void GetRecoveryTimes_OrdersByMedianDescending()
        {
            var recoveries = new List<AnalyticsRecoveryTime>
            {
                new() { ProjectName = "Quick", MedianRecoveryHours = 0.5m, AvgRecoveryHours = 1m, SampleCount = 20 },
                new() { ProjectName = "Slow", MedianRecoveryHours = 48m, AvgRecoveryHours = 50m, SampleCount = 4 }
            };
            var dbSet = DbContextMock.GetQueryableMockDbSet(recoveries);
            _context.AnalyticsRecoveryTime.Returns(dbSet);

            var result = _source.GetRecoveryTimes().ToList();

            Assert.AreEqual("Slow", result[0].ProjectName);
            Assert.AreEqual(48, result[0].MedianRecoveryHours);
            Assert.AreEqual(50, result[0].AvgRecoveryHours);
            Assert.AreEqual(4, result[0].SampleCount);
        }

        [TestMethod]
        public void NewAnalyticsGetters_EmptyTables_ReturnEmpty()
        {
            var emptyOutcomes = DbContextMock.GetQueryableMockDbSet(new List<AnalyticsMonthlyOutcome>());
            var emptyWaits = DbContextMock.GetQueryableMockDbSet(new List<AnalyticsEnvironmentWait>());
            var emptyDurations = DbContextMock.GetQueryableMockDbSet(new List<AnalyticsProjectDuration>());
            var emptyReliability = DbContextMock.GetQueryableMockDbSet(new List<AnalyticsComponentReliability>());
            var emptyRecoveries = DbContextMock.GetQueryableMockDbSet(new List<AnalyticsRecoveryTime>());
            _context.AnalyticsMonthlyOutcome.Returns(emptyOutcomes);
            _context.AnalyticsEnvironmentWait.Returns(emptyWaits);
            _context.AnalyticsProjectDuration.Returns(emptyDurations);
            _context.AnalyticsComponentReliability.Returns(emptyReliability);
            _context.AnalyticsRecoveryTime.Returns(emptyRecoveries);

            Assert.AreEqual(0, _source.GetMonthlyOutcomes().Count());
            Assert.AreEqual(0, _source.GetEnvironmentWaitTimes().Count());
            Assert.AreEqual(0, _source.GetProjectDurations().Count());
            Assert.AreEqual(0, _source.GetComponentReliability().Count());
            Assert.AreEqual(0, _source.GetRecoveryTimes().Count());
        }
    }
}
