using System.Text.Json;
using Dorc.ApiModel;
using Dorc.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RestSharp;
using Tools.PostRestoreEndurCLI;

namespace Tools.RequestCLI.Tests;

[TestClass]
[TestCategory("Database")]
[DoNotParallelize]
public class PostRestoreEndurTests
{
    private static string _masterConnectionString = null!;
    private static string _databaseName = null!;
    private static string _serverInstance = null!;

    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DOrcConnectionString")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=True;TrustServerCertificate=True;";

        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
        _masterConnectionString = builder.ConnectionString;
        _serverInstance = builder.DataSource;
        _databaseName = "dorc_endur_refresh_" + Guid.NewGuid().ToString("N");

        using var sql = new SqlConnection(_masterConnectionString);
        sql.Open();
        using (var createDb = new SqlCommand($"CREATE DATABASE [{_databaseName}]", sql))
        {
            createDb.ExecuteNonQuery();
        }

        builder.InitialCatalog = _databaseName;
        using var db = new SqlConnection(builder.ConnectionString);
        db.Open();
        using var schemaCmd = new SqlCommand(@"
            CREATE TABLE dbo.USER_AppConfig (VALUE NVARCHAR(200));
            INSERT INTO dbo.USER_AppConfig VALUES ('ProdVal');
            CREATE TABLE dbo.USER_EODPriceExtract (Parameter NVARCHAR(100), VALUE NVARCHAR(200));
            INSERT INTO dbo.USER_EODPriceExtract VALUES ('ExcelFilePath', '\\\\prod\\share');
            CREATE TABLE dbo.USER_PriceExtract (Parameter NVARCHAR(100), VALUE NVARCHAR(200));
            INSERT INTO dbo.USER_PriceExtract VALUES ('ExcelFilePath', '\\\\prod\\share');
            CREATE TABLE dbo.USER_bo_invoice_sequence (inv_num_prefix NVARCHAR(10), invoice_seq NVARCHAR(50));
            INSERT INTO dbo.USER_bo_invoice_sequence VALUES ('UK', '1'), ('SG', '1'), ('US', '1');
            CREATE TABLE dbo.USER_SERVICEMONITOR (MONITOR INT);
            INSERT INTO dbo.USER_SERVICEMONITOR VALUES (1);

            CREATE TABLE dbo.service_mgr (
                id INT,
                service_name NVARCHAR(100),
                login_name NVARCHAR(100),
                workstation_name NVARCHAR(100),
                default_service INT,
                app_login_name NVARCHAR(100),
                curr_date DATETIME,
                user_id INT
            );
            INSERT INTO dbo.service_mgr VALUES
                (1, 'Appserver01', 'old_svc', 'old_ws', 1, 'endurappsvc_prod', GETDATE(), 1),
                (2, 'Appserver02', 'old_svc', 'old_ws2', 0, 'unmatched_tag', GETDATE(), 1);

            CREATE TABLE dbo.personnel (password_date DATETIME, password_never_expires INT);
            INSERT INTO dbo.personnel VALUES ('2020-01-01', 0), ('2020-01-01', 1);
        ", db);
        schemaCmd.ExecuteNonQuery();

        using var proc1 = new SqlCommand(@"
            CREATE PROCEDURE dbo.update_service_mgr
                @id INT,
                @service_name NVARCHAR(100),
                @login_name NVARCHAR(100),
                @workstation_name NVARCHAR(100),
                @default_service INT,
                @app_login_name NVARCHAR(100),
                @curr_date DATETIME,
                @user_id INT
            AS
            BEGIN
                UPDATE dbo.service_mgr
                SET login_name = @login_name, workstation_name = @workstation_name
                WHERE id = @id;
            END
        ", db);
        proc1.ExecuteNonQuery();
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        if (string.IsNullOrEmpty(_databaseName) || string.IsNullOrEmpty(_masterConnectionString))
            return;

        using var sql = new SqlConnection(_masterConnectionString);
        sql.Open();
        using var cmd = new SqlCommand($"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]", sql);
        cmd.ExecuteNonQuery();
    }

    [TestMethod]
    public void RefreshEndur_ScrubsUserTablesRemapsServiceManagerByTagSubstringAndRecordsEnvironmentHistory()
    {
        var api = Substitute.For<IDorcApiClient>();
        api.Call<DatabaseApiModel>(Endpoints.RefDataDatabasesByTag, Method.Get, Arg.Any<Dictionary<string, string>>(), Arg.Any<string>())
            .Returns(new ApiResult<DatabaseApiModel>
            {
                IsModelValid = true,
                Value = new DatabaseApiModel { ServerName = _serverInstance, Name = _databaseName }
            });

        api.Call<List<ServerApiModel>>(Endpoints.RefDataServersAppServersByEnvName, Method.Get, Arg.Any<Dictionary<string, string>>(), Arg.Any<string>())
            .Returns(new ApiResult<List<ServerApiModel>>
            {
                IsModelValid = true,
                Value = new List<ServerApiModel>
                {
                    new() { Name = "DV01-APPSRV-01", Tags = new[] { "role_app", "endurappsvc_prod_1" } }
                }
            });

        string? capturedHistoryBody = null;
        api.Call<ApiBoolResult>(Endpoints.RefDataEnvironmentsHistory, Method.Post, Arg.Any<Dictionary<string, string>>(), Arg.Do<string>(b => capturedHistoryBody = b))
            .Returns(new ApiResult<ApiBoolResult>
            {
                IsModelValid = true,
                Value = new ApiBoolResult { Result = true }
            });

        var refresh = new RefreshEndur(NullLogger.Instance, api);

        Assert.IsTrue(refresh.UpdateUserTablesToDummyValues("DV 01"));
        Assert.IsTrue(refresh.UpdateAppServerDetails("DV 01", "SVC-DV01-ENDUR"));
        Assert.IsTrue(refresh.UpdateEndurUsers("DV 01"));
        refresh.UpdateEnvironmentHistory("DV 01", "backup_2026.bak", "Self service refresh", "engineer@sefe.eu", "SelfService");

        var dbBuilder = new SqlConnectionStringBuilder(_masterConnectionString) { InitialCatalog = _databaseName };
        using var db = new SqlConnection(dbBuilder.ConnectionString);
        db.Open();

        using (var cmd = new SqlCommand("SELECT VALUE FROM dbo.USER_AppConfig", db))
        {
            Assert.AreEqual("Dummy Value After Restore", (string)cmd.ExecuteScalar()!);
        }

        using (var cmd = new SqlCommand("SELECT login_name, workstation_name FROM dbo.service_mgr WHERE id = 1", db))
        {
            using var reader = cmd.ExecuteReader();
            Assert.IsTrue(reader.Read());
            Assert.AreEqual("SVC-DV01-ENDUR", reader.GetString(0));
            Assert.AreEqual("DV01-APPSRV-01", reader.GetString(1));
        }

        using (var cmd = new SqlCommand("SELECT login_name, workstation_name FROM dbo.service_mgr WHERE id = 2", db))
        {
            using var reader = cmd.ExecuteReader();
            Assert.IsTrue(reader.Read());
            Assert.AreEqual("NA", reader.GetString(0));
            Assert.AreEqual("NA", reader.GetString(1));
        }

        Assert.IsNotNull(capturedHistoryBody);
        var historyDto = JsonSerializer.Deserialize<UpdateEnvironmentHistoryRequest>(capturedHistoryBody!)!;
        Assert.AreEqual("DV 01", historyDto.EnvName);
        Assert.AreEqual("backup_2026.bak", historyDto.BackupFile);
        Assert.AreEqual("engineer@sefe.eu", historyDto.UpdatedBy);
    }
}
