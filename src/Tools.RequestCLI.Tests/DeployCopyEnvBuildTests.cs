using System.Text.Json;
using Dorc.ApiModel;
using Dorc.Core;
using NSubstitute;
using RestSharp;
using Tools.DeployCopyEnvBuildCLI;
using CopyProgram = Tools.DeployCopyEnvBuildCLI.Program;

namespace Tools.RequestCLI.Tests;

[TestClass]
public class DeployCopyEnvBuildTests
{
    [TestMethod]
    public void ParseArguments_ExtractsSourceTargetProjectAndSemicolonComponents()
    {
        var parsed = CopyProgram.ParseArguments(new[]
        {
            "/sourceenv:QA 01",
            "/targetenv:DV 01",
            "/project:Endur",
            "/components:CompA;CompB"
        });

        Assert.AreEqual("QA 01", parsed.SourceEnv);
        Assert.AreEqual("DV 01", parsed.TargetEnv);
        Assert.AreEqual("Endur", parsed.Project);
        Assert.AreEqual("CompA;CompB", parsed.Components);
    }

    [TestMethod]
    public void ExecuteCopyEnvBuild_RejectsProductionTargetEnvironment()
    {
        var api = Substitute.For<IDorcApiClient>();
        api.Call<List<EnvironmentApiModel>>(
                Endpoints.RefDataEnvironments,
                Method.Get,
                Arg.Is<Dictionary<string, string>>(d => d["env"] == "PR 02"),
                Arg.Any<string>())
            .Returns(new ApiResult<List<EnvironmentApiModel>>
            {
                IsModelValid = true,
                Value = new List<EnvironmentApiModel>
                {
                    new() { EnvironmentName = "PR 02", EnvironmentIsProd = true }
                }
            });

        var args = new Arguments
        {
            SourceEnv = "ST 01",
            TargetEnv = "PR 02",
            Project = "Endur",
            Components = "CompA"
        };

        var exitCode = CopyProgram.ExecuteCopyEnvBuild(api, args);

        Assert.AreEqual(1, exitCode);
        api.DidNotReceive().Call<CopyEnvBuildResponseDto>(Endpoints.CopyEnvBuild, Method.Post, Arg.Any<Dictionary<string, string>>(), Arg.Any<string>());
    }

    [TestMethod]
    public void ExecuteCopyEnvBuild_WhenTargetEnvironmentIsNonProd_SubmitsCopyRequestAndReturnsZero()
    {
        var api = Substitute.For<IDorcApiClient>();
        api.Call<List<EnvironmentApiModel>>(
                Endpoints.RefDataEnvironments,
                Method.Get,
                Arg.Is<Dictionary<string, string>>(d => d["env"] == "DV 01"),
                Arg.Any<string>())
            .Returns(new ApiResult<List<EnvironmentApiModel>>
            {
                IsModelValid = true,
                Value = new List<EnvironmentApiModel>
                {
                    new() { EnvironmentName = "DV 01", EnvironmentIsProd = false }
                }
            });

        string? capturedBody = null;
        api.Call<CopyEnvBuildResponseDto>(
                Endpoints.CopyEnvBuild,
                Method.Post,
                Arg.Any<Dictionary<string, string>>(),
                Arg.Do<string>(b => capturedBody = b))
            .Returns(new ApiResult<CopyEnvBuildResponseDto>
            {
                IsModelValid = true,
                Value = new CopyEnvBuildResponseDto
                {
                    Success = true,
                    RequestIds = new List<int> { 501, 502 }
                }
            });

        var args = new Arguments
        {
            SourceEnv = "QA 01",
            TargetEnv = "DV 01",
            Project = "Endur",
            Components = "CompA;CompB"
        };

        var exitCode = CopyProgram.ExecuteCopyEnvBuild(api, args);

        Assert.AreEqual(0, exitCode);
        Assert.IsNotNull(capturedBody);
        var dto = JsonSerializer.Deserialize<CopyEnvBuildDto>(capturedBody!)!;
        Assert.AreEqual("QA 01", dto.SourceEnv);
        Assert.AreEqual("DV 01", dto.TargetEnv);
        Assert.AreEqual("Endur", dto.Project);
        Assert.AreEqual("CompA;CompB", dto.Components);
    }
}
