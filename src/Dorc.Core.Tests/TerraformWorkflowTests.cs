using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class TerraformWorkflowTests
    {
        [TestMethod]
        public void ComponentType_TerraformEnum_HasCorrectValue()
        {
            // Arrange & Act
            var terraformType = ComponentType.Terraform;
            var powershellType = ComponentType.PowerShell;

            // Assert
            Assert.AreEqual("Terraform", terraformType.ToString());
            Assert.AreEqual("PowerShell", powershellType.ToString());
        }

        [TestMethod]
        public void DeploymentResultStatus_WaitingConfirmation_HasCorrectValue()
        {
            // Arrange & Act
            var waitingStatus = DeploymentResultStatus.WaitingConfirmation;
            var confirmedStatus = DeploymentResultStatus.Confirmed;

            // Assert
            Assert.AreEqual("WaitingConfirmation", waitingStatus.Value);
            Assert.AreEqual("Confirmed", confirmedStatus.Value);
        }

        [TestMethod]
        public void DeploymentRequestStatus_WaitingConfirmation_HasCorrectValue()
        {
            // Arrange & Act
            var status = DeploymentRequestStatus.WaitingConfirmation;

            // Assert
            Assert.AreEqual(DeploymentRequestStatus.WaitingConfirmation, status);
        }

        [TestMethod]
        public void ComponentApiModel_DefaultComponentType_IsPowerShell()
        {
            // Arrange & Act
            var component = new ComponentApiModel();

            // Assert
            Assert.AreEqual(ComponentType.PowerShell, component.ComponentType);
        }

        [TestMethod]
        public void TerraformPlanApiModel_InitializedCorrectly()
        {
            // Arrange & Act
            var plan = new TerraformPlanApiModel
            {
                DeploymentResultId = 123,
                PlanContent = "Test plan",
                BlobUrl = "https://test.blob.core.windows.net/plans/test.tfplan",
                CreatedAt = DateTime.UtcNow,
                Status = "WaitingConfirmation"
            };

            // Assert
            Assert.AreEqual(123, plan.DeploymentResultId);
            Assert.AreEqual("Test plan", plan.PlanContent);
            Assert.AreEqual("WaitingConfirmation", plan.Status);
            Assert.IsTrue(plan.BlobUrl.Contains("test.tfplan"));
        }
    }
}