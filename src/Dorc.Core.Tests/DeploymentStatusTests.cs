using Dorc.ApiModel;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class DeploymentStatusTests
    {
        [TestMethod]
        public void DeploymentRequestStatus_HasCompletedWithDisabledSteps()
        {
            // Arrange & Act
            var status = DeploymentRequestStatus.CompletedWithDisabledSteps;
            
            // Assert
            Assert.AreEqual("CompletedWithDisabledSteps", status.ToString());
        }
        
        [TestMethod]
        public void DeploymentRequestStatus_CompletedWithDisabledSteps_IsValidStatus()
        {
            // Arrange
            var validStatuses = Enum.GetValues<DeploymentRequestStatus>();
            
            // Act & Assert
            Assert.IsTrue(validStatuses.Contains(DeploymentRequestStatus.CompletedWithDisabledSteps));
        }
    }
}