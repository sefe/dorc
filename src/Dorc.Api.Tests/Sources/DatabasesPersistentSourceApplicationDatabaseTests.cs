using Dorc.Api.Tests.Mocks;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Dorc.Api.Tests.Sources;

[TestClass]
public class DatabasesPersistentSourceApplicationDatabaseTests
{
    [TestMethod]
    public void GetApplicationDatabaseForEnvFilter_ResolvesViaDatabaseAccessReader()
    {
        var envEntity = new Dorc.PersistentData.Model.Environment
        {
            Id = 5,
            Name = "EnvFilter_DV",
            ThinClientServer = "thin01"
        };
        var dbEntity = new Database
        {
            Id = 42,
            Name = "AppDb_42",
            ServerName = "sql-filter",
            Environments = new List<Dorc.PersistentData.Model.Environment> { envEntity }
        };
        envEntity.Databases = new List<Database> { dbEntity };

        var envSet = DbContextMock.GetQueryableMockDbSet(new List<Dorc.PersistentData.Model.Environment> { envEntity });
        var dbSet = DbContextMock.GetQueryableMockDbSet(new List<Database> { dbEntity });

        var context = Substitute.For<IDeploymentContext>();
        context.Environments.Returns(envSet);
        context.Databases.Returns(dbSet);
        var factory = Substitute.For<IDeploymentContextFactory>();
        factory.GetContext().Returns(context);

        var reader = Substitute.For<IDatabaseAccessReader>();
        reader.FindDatabaseWithNativePrincipal(Arg.Is<IReadOnlyCollection<int>>(ids => ids.Contains(42)), "app_login")
            .Returns(42);
        reader.FindDatabaseWithNativePrincipal(Arg.Any<IReadOnlyCollection<int>>(), "unknown_login")
            .Returns((int?)null);

        var source = new DatabasesPersistentSource(
            factory,
            Substitute.For<IRolePrivilegesChecker>(),
            Substitute.For<IClaimsPrincipalReader>(),
            reader);

        var resolved = source.GetApplicationDatabaseForEnvFilter("app_login", "THIN01", "ENVFILTER_DV");
        Assert.IsNotNull(resolved);
        Assert.AreEqual(42, resolved.Id);
        Assert.IsNull(source.GetApplicationDatabaseForEnvFilter("unknown_login", "thin01", "EnvFilter_DV"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            source.GetApplicationDatabaseForEnvFilter("app_login", "other_thin", "EnvFilter_DV"));
    }
}
