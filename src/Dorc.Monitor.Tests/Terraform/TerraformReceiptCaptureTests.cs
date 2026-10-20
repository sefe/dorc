using System.IO.Compression;
using System.Text.Json;
using Dorc.Core.AzureStorageAccount;
using Dorc.Monitor.Terraform;
using Dorc.PersistentData.Model;

namespace Dorc.Monitor.Tests.Terraform;

[TestClass]
public class TerraformReceiptCaptureTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CatalogSnapshot_PreservesFalseAndRequestOverridePrecedenceWithoutMutatingSharedProperties(bool overridden)
    {
        var component = new Dorc.ApiModel.ComponentApiModel { ComponentId = 3, TerraformTemplateVersion = "2.0.0" };
        var original = new Dictionary<string, Dorc.ApiModel.MonitorRunnerApi.VariableValue>
        {
            ["create_service"] = new() { Value = "true" }
        };
        var detail = new Dorc.Core.DeploymentRequestDetail();
        if (overridden) detail.Properties.Add(new Dorc.Core.PropertyPair("create_service", "true"));
        var snapshot = new CatalogRequestInputs
        {
            ComponentId = 3, TemplateName = "database", TemplateVersion = "1.0.0",
            InputsJson = "{\"create_service\":\"false\"}"
        };
        var result = TerraformCatalogRequestSnapshot.Apply(component, original,
            new Dorc.Core.DeploymentRequestDetailSerializer().Serialize(detail), snapshot);
        Assert.AreEqual(overridden ? "true" : "false", result["create_service"].Value);
        Assert.AreEqual("true", original["create_service"].Value);
        Assert.AreEqual("1.0.0", component.TerraformTemplateVersion);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Receipt_UsesActualArchiveProvenanceAndRefusesMismatchedIdentity(bool wrongResult)
    {
        var directory = Path.Combine(System.Environment.CurrentDirectory, "receipt-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "source.zip");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("_dorc_provenance.json").Open()))
                writer.Write(JsonSerializer.Serialize(new
                {
                    RequestId = 7, DeploymentResultId = wrongResult ? 99 : 42,
                    TemplateName = "planned-module", TemplateVersion = "1.0.0",
                    GitCommitSha = new string('a', 40), StateKey = "original/state.tfstate",
                    ArchivedAtUtc = DateTimeOffset.Parse("2026-10-20T10:00:00Z")
                }));
            var planContent = Path.Combine(directory, "plan.txt");
            var plan = Path.Combine(directory, "plan.bin");
            File.WriteAllText(planContent, "reviewed plan");
            File.WriteAllBytes(plan, new byte[] { 1, 2, 3 });
            var receipt = new TerraformDeploymentReceipt { DeploymentRequestId = 7, DeploymentResultId = 42 };
            if (wrongResult)
            {
                Assert.Throws<InvalidDataException>(() => TerraformReceiptCapture.ReadPlannedSource(receipt, path, planContent, plan));
                return;
            }
            TerraformReceiptCapture.ReadPlannedSource(receipt, path, planContent, plan);
            Assert.AreEqual("planned-module", receipt.TemplateName);
            Assert.AreEqual("1.0.0", receipt.TemplateVersion);
            Assert.AreEqual("original/state.tfstate", receipt.StateKey);
            Assert.AreEqual(TerraformPlanArtifactIntegrity.HashPlanContent("reviewed plan"), receipt.PlanContentSha256);
            Assert.AreEqual(64, receipt.SourceSha256.Length);
            Assert.AreEqual(64, receipt.PlanSha256.Length);
        }
        finally { Directory.Delete(directory, true); }
    }
}
