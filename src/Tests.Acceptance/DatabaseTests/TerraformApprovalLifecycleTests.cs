using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Security.Claims;
using System.Security.Principal;

namespace Tests.Acceptance.DatabaseTests
{
    [TestClass]
    public class TerraformApprovalLifecycleTests : DatabaseTestBase
    {
        private RequestsPersistentSource CreateSource() =>
            new(new DeploymentContextFactory(EphemeralConnectionString), new UnusedCaller());

        private void Seed()
        {
            PublishDacpac();
            ExecuteEphemeral("""
                INSERT INTO [deploy].[DeploymentRequest] ([RequestDetails], [UserName], [Status])
                VALUES (N'<DeploymentRequestDetail />', N'requester', N'WaitingConfirmation');
                INSERT INTO [deploy].[Component] ([Name], [StopOnFailure], [ComponentType])
                VALUES (N'first', 0, 1), (N'second', 0, 1), (N'pending', 0, 1);
                INSERT INTO [deploy].[DeploymentResult] ([DeploymentRequestId], [ComponentId], [Status])
                VALUES (1, 1, N'WaitingConfirmation'), (1, 2, N'WaitingConfirmation'), (1, 3, N'Pending');
                """);
        }

        private static DeploymentResultApiModel Result(int id) => new()
        {
            Id = id, RequestId = 1, ComponentId = id, Status = DeploymentResultStatus.WaitingConfirmation.ToString()
        };

        private static TerraformPlanApproval Approval(int id) => new()
        {
            DeploymentResultId = id, DeploymentRequestId = 1, ApprovedBy = @"CORP\approver",
            ApprovedAt = DateTimeOffset.UtcNow, PlanSha256 = new string('A', 64),
            SourceSha256 = new string('B', 64), PlanContentSha256 = new string('C', 64)
        };

        [TestMethod]
        [TestCategory("Database")]
        public void Confirm_CancelledParent_CannotBeResurrectedOrAuditedAsApproved()
        {
            Seed();
            ExecuteEphemeral("UPDATE [deploy].[DeploymentRequest] SET [Status] = N'Cancelled'");

            Assert.IsFalse(CreateSource().TryConfirmTerraformPlan(Result(1), Approval(1)));
            Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DeploymentRequest] WHERE [Status] = N'Cancelled'"));
            Assert.AreEqual(2, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DeploymentResult] WHERE [Status] = N'WaitingConfirmation'"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[TerraformPlanApproval]"));
        }

        [TestMethod]
        [TestCategory("Database")]
        public void Confirm_ConcurrentSiblings_RecordsEachApprovalWithoutApprovingOtherResults()
        {
            Seed();
            var tasks = new[] { 1, 2 }.Select(id => Task.Run(() =>
                CreateSource().TryConfirmTerraformPlan(Result(id), Approval(id)))).ToArray();

            Task.WaitAll(tasks);

            Assert.IsTrue(tasks.All(task => task.Result));
            Assert.AreEqual(2, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[TerraformPlanApproval]"));
            Assert.AreEqual(2, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DeploymentResult] WHERE [Status] = N'Confirmed'"));
            Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DeploymentResult] WHERE [Status] = N'Pending'"));
        }

        [TestMethod]
        [TestCategory("Database")]
        public void Decline_CancelsWaitingConfirmedAndPendingSiblings_AndPreservesApprovalHistory()
        {
            Seed();
            var source = CreateSource();
            Assert.IsTrue(source.TryConfirmTerraformPlan(Result(1), Approval(1)));

            Assert.IsTrue(source.TryDeclineTerraformPlan(Result(2), @"CORP\decliner", DateTimeOffset.UtcNow));

            Assert.AreEqual(3, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DeploymentResult] WHERE [Status] = N'Cancelled'"));
            Assert.IsFalse(source.TryConfirmTerraformPlan(Result(1), Approval(1)));
            ExecuteEphemeral("DELETE FROM [deploy].[DeploymentResult]");
            Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[TerraformPlanApproval]"));
        }

        [TestMethod]
        [TestCategory("Database")]
        public void Confirm_AuditFailure_RollsBackParentAndResult()
        {
            Seed();
            var approval = Approval(1);
            approval.ApprovedBy = new string('x', 257);

            Assert.Throws<DbUpdateException>(() =>
                CreateSource().TryConfirmTerraformPlan(Result(1), approval));

            Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DeploymentRequest] WHERE [Status] = N'WaitingConfirmation'"));
            Assert.AreEqual(2, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DeploymentResult] WHERE [Status] = N'WaitingConfirmation'"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[TerraformPlanApproval]"));
        }

        [TestMethod]
        [TestCategory("Database")]
        public void Monitor_StartAndCompletion_CannotOverwriteCancellation()
        {
            Seed();
            ExecuteEphemeral("UPDATE [deploy].[DeploymentRequest] SET [Status] = N'Cancelled'");
            var source = CreateSource();

            Assert.IsFalse(source.SetRequestStartStatus(new DeploymentRequestApiModel
            {
                Id = 1, Status = DeploymentRequestStatus.Confirmed.ToString()
            }, DeploymentRequestStatus.Running, DateTimeOffset.UtcNow));
            source.SetRequestCompletionStatus(1, DeploymentRequestStatus.Completed, DateTimeOffset.UtcNow);

            Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DeploymentRequest] WHERE [Status] = N'Cancelled'"));
        }

        private sealed class UnusedCaller : IClaimsPrincipalReader
        {
            public string GetUserName(IPrincipal user) => throw new NotSupportedException();
            public string GetUserId(ClaimsPrincipal user) => throw new NotSupportedException();
            public string GetUserLogin(IPrincipal user) => throw new NotSupportedException();
            public string GetUserFullDomainName(IPrincipal user) => throw new NotSupportedException();
            public string GetUserSafeIdentifier(IPrincipal user) => throw new NotSupportedException();
            public string GetUserEmail(ClaimsPrincipal user) => throw new NotSupportedException();
            public List<string> GetSidsForUser(IPrincipal user) => throw new NotSupportedException();
        }
    }
}
