using System.Security.Claims;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dorc.Api.Tests;

[TestClass]
public class EnvironmentDetailsResolutionTests
{
    [TestMethod]
    public void GetEnvironmentsDetails_PopulatesNativePrincipalsFromDatabaseAccessReaderAndHandlesNullEnvironment()
    {
        var reader = Substitute.For<IDatabaseAccessReader>();
        var databases = Substitute.For<IDatabasesPersistentSource>();
        var environments = Substitute.For<IEnvironmentsPersistentSource>();
        var servers = Substitute.For<IServersPersistentSource>();
        var apiServices = new ApiServices(
            Substitute.For<IManageProjectsPersistentSource>(),
            reader,
            Substitute.For<IProjectsPersistentSource>(),
            databases,
            environments,
            servers,
            NullLogger<ApiServices>.Instance);

        databases.GetDatabasesForEnvId(10).Returns([
            new DatabaseApiModel { Id = 1, Name = "AppDb", Tags = ["Endur", "Primary"] },
            new DatabaseApiModel { Id = 2, Name = "OtherDb", Tags = ["Reporting"] },
            new DatabaseApiModel { Id = 3, Name = "UntaggedDb", Tags = null }
        ]);
        var expectedUsers = new List<UserApiModel> { new() { Id = 5, LoginId = "app_login", DisplayName = "app_login" } };
        reader.GetNativePrincipalsForDatabases(Arg.Is<IReadOnlyCollection<int>>(ids => ids.Count == 1 && ids.Contains(1)))
            .Returns(expectedUsers);

        environments.GetEnvironment(10, Arg.Any<ClaimsPrincipal>()).Returns(new EnvironmentApiModel
        {
            EnvironmentId = 10,
            EnvironmentName = "DV02",
            Details = new EnvironmentDetailsApiModel { FileShare = "\\\\share", Description = "Desc" }
        });

        var details = apiServices.GetEnvironmentsDetails(10, new ClaimsPrincipal());
        Assert.AreEqual("DV02", details.EnvironmentName);
        Assert.AreEqual(1, details.EndurUsers.Count());

        environments.GetEnvironment(99, Arg.Any<ClaimsPrincipal>()).Returns((EnvironmentApiModel?)null);
        var missing = apiServices.GetEnvironmentsDetails(99, new ClaimsPrincipal());
        Assert.AreEqual("", missing.EnvironmentName);
    }
}
