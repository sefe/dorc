using System.Text.Json;
using Dorc.ApiModel;
using Dorc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RestSharp;
using Tools.PropertyValueCreationCLI;

namespace Tools.RequestCLI.Tests;

[TestClass]
public class PropertyValueCreationTests
{
    [TestMethod]
    public void DeployCsvFileReader_ParsesQuotedCommasSecureFlagsAndEnvironments()
    {
        var reader = new DeployCsvFileReader();
        var rows = reader.GetValues(new[]
        {
            "PlainProp,0,SimpleValue,DV 01",
            "SecretProp,1,\"ValueWith,InternalComma\",QA 01",
            "   ,0,IgnoredBlankRow,DV 01"
        }).ToList();

        Assert.HasCount(2, rows);
        Assert.AreEqual("PlainProp", rows[0].PropertyName);
        Assert.IsFalse(rows[0].IsSecure);
        Assert.AreEqual("SimpleValue", rows[0].Value);
        Assert.AreEqual("DV 01", rows[0].Environment);

        Assert.AreEqual("SecretProp", rows[1].PropertyName);
        Assert.IsTrue(rows[1].IsSecure);
        Assert.AreEqual("ValueWith,InternalComma", rows[1].Value);
        Assert.AreEqual("QA 01", rows[1].Environment);
    }

    [TestMethod]
    public void Application_CheckFileAndRun_ValidatesEnvironmentsAnd64CharLimit_ThenPostsPropertiesAndValues()
    {
        var tempCsv = Path.Join(Path.GetTempPath(), $"dorc-props-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllLines(tempCsv, new[]
            {
                "App_Port,0,8080,DV 01",
                "App_ApiKey,1,s3cr3t,DV 01",
                "App_Port,0,9090,"
            });

            var api = Substitute.For<IDorcApiClient>();
            api.Call<List<EnvironmentApiModel>>(
                    Endpoints.RefDataEnvironments,
                    Method.Get,
                    Arg.Is<Dictionary<string, string>>(d => d["env"] == "DV 01"),
                    Arg.Any<string>())
                .Returns(new ApiResult<List<EnvironmentApiModel>>
                {
                    IsModelValid = true,
                    Value = new List<EnvironmentApiModel> { new() { EnvironmentName = "DV 01" } }
                });

            string? capturedPropsBody = null;
            api.Call<List<Response>>(Endpoints.Properties, Method.Post, Arg.Any<Dictionary<string, string>>(), Arg.Do<string>(b => capturedPropsBody = b))
                .Returns(new ApiResult<List<Response>>
                {
                    IsModelValid = true,
                    Value = new List<Response> { new() { Item = "App_Port", Status = "success" } }
                });

            string? capturedValuesBody = null;
            api.Call<List<Response>>(Endpoints.PropertyValues, Method.Post, Arg.Any<Dictionary<string, string>>(), Arg.Do<string>(b => capturedValuesBody = b))
                .Returns(new ApiResult<List<Response>>
                {
                    IsModelValid = true,
                    Value = new List<Response> { new() { Item = "App_Port", Status = "success" } }
                });

            var app = new Tools.PropertyValueCreationCLI.Program.Application(api, NullLogger.Instance);
            app.CheckFile(tempCsv);
            app.Run(tempCsv);

            Assert.IsNotNull(capturedPropsBody);
            var postedProps = JsonSerializer.Deserialize<List<PropertyApiModel>>(capturedPropsBody!)!;
            Assert.HasCount(2, postedProps, "Duplicate PropertyName rows should be deduplicated before POST /Properties.");
            Assert.IsTrue(postedProps.Any(p => p.Name == "App_ApiKey" && p.Secure));

            Assert.IsNotNull(capturedValuesBody);
            var postedValues = JsonSerializer.Deserialize<List<PropertyValueDto>>(capturedValuesBody!)!;
            Assert.HasCount(3, postedValues);
        }
        finally
        {
            if (File.Exists(tempCsv))
                File.Delete(tempCsv);
        }
    }

    [TestMethod]
    public void Application_CheckFile_RejectsMissingEnvironmentAndOverlongPropertyName()
    {
        var tempCsv = Path.Join(Path.GetTempPath(), $"dorc-props-invalid-{Guid.NewGuid():N}.csv");
        try
        {
            var api = Substitute.For<IDorcApiClient>();
            api.Call<List<EnvironmentApiModel>>(Endpoints.RefDataEnvironments, Method.Get, Arg.Any<Dictionary<string, string>>(), Arg.Any<string>())
                .Returns(new ApiResult<List<EnvironmentApiModel>>
                {
                    IsModelValid = true,
                    Value = new List<EnvironmentApiModel>()
                });

            File.WriteAllLines(tempCsv, new[] { "PropA,0,Val,NONEXISTENT_ENV" });
            var app = new Tools.PropertyValueCreationCLI.Program.Application(api, NullLogger.Instance);
            Assert.ThrowsExactly<MissingFieldException>(() => app.CheckFile(tempCsv));

            var longPropName = new string('X', 65);
            File.WriteAllLines(tempCsv, new[] { $"{longPropName},0,Val," });
            Assert.ThrowsExactly<OverflowException>(() => app.CheckFile(tempCsv));
        }
        finally
        {
            if (File.Exists(tempCsv))
                File.Delete(tempCsv);
        }
    }

    [TestMethod]
    public void ProgramMain_WithEmptyArgs_ReturnsNonZeroExitCode()
    {
        var exitCode = Tools.PropertyValueCreationCLI.Program.Main(Array.Empty<string>());
        Assert.AreEqual(1, exitCode);
        Assert.AreEqual(1, Environment.ExitCode);
        Environment.ExitCode = 0;
    }

    [TestMethod]
    public void Application_Run_WhenPropertyValuesPostReturnsNonSuccessItem_ThrowsInvalidOperationException()
    {
        var tempCsv = Path.Join(Path.GetTempPath(), $"dorc-props-fail-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllLines(tempCsv, new[] { "App_Port,0,8080,DV 01" });

            var api = Substitute.For<IDorcApiClient>();
            api.Call<List<Response>>(Endpoints.Properties, Method.Post, Arg.Any<Dictionary<string, string>>(), Arg.Any<string>())
                .Returns(new ApiResult<List<Response>>
                {
                    IsModelValid = true,
                    Value = new List<Response> { new() { Item = "App_Port", Status = "success" } }
                });
            api.Call<List<Response>>(Endpoints.PropertyValues, Method.Post, Arg.Any<Dictionary<string, string>>(), Arg.Any<string>())
                .Returns(new ApiResult<List<Response>>
                {
                    IsModelValid = true,
                    Value = new List<Response>
                    {
                        new() { Item = new { Value = "SuperSecret123" }, Status = "User does not have write permission for environment 'DV 01'" },
                        new() { Item = "ExtraItem", Status = "Extra item failed" }
                    }
                });

            var app = new Tools.PropertyValueCreationCLI.Program.Application(api, NullLogger.Instance);
            var ex = Assert.ThrowsExactly<InvalidOperationException>(() => app.Run(tempCsv));
            StringAssert.Contains(ex.Message, "App_Port (DV 01)");
            StringAssert.Contains(ex.Message, "Item[1]");
            Assert.IsFalse(ex.Message.Contains("SuperSecret123", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(tempCsv))
                File.Delete(tempCsv);
        }
    }
}
