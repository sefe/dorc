using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

public partial class RequestsPersistentSource
{
    private static string SubmissionCallerHash(CatalogSubmissionIdentity submission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(submission.Caller);
        if (submission.Key == Guid.Empty) throw new ArgumentException("A nonempty submission key is required.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "catalog-instantiate-submit\n" + submission.Caller.Trim().ToUpperInvariant())));
    }

    private static void VerifySubmissionPayload(CatalogSubmission saved, CatalogSubmissionIdentity supplied)
    {
        var fingerprint = HMACSHA256.HashData(saved.PayloadSalt, Encoding.UTF8.GetBytes(supplied.CanonicalPayload));
        if (!CryptographicOperations.FixedTimeEquals(saved.PayloadFingerprint, fingerprint))
            throw new CatalogSubmissionConflictException();
    }

    public TerraformTemplateInstantiateResponseApiModel? FindCatalogSubmission(CatalogSubmissionIdentity submission)
    {
        var callerHash = SubmissionCallerHash(submission);
        using var context = _contextFactory.GetContext();
        var saved = context.CatalogSubmissions.AsNoTracking().SingleOrDefault(
            row => row.CallerHash == callerHash && row.SubmissionKey == submission.Key);
        if (saved == null) return null;
        VerifySubmissionPayload(saved, submission);
        return new TerraformTemplateInstantiateResponseApiModel
        {
            Component = JsonSerializer.Deserialize<ComponentApiModel>(saved.ComponentJson)!,
            RequestId = saved.DeploymentRequestId,
            RequestStatus = "Pending"
        };
    }

    public int SubmitCatalogRequest(DeploymentRequest request, CatalogSubmissionIdentity submission)
    {
        var callerHash = SubmissionCallerHash(submission);
        using var strategyContext = _contextFactory.GetContext();
        return strategyContext.Database.CreateExecutionStrategy().Execute(() =>
        {
            using var context = _contextFactory.GetContext();
            using var transaction = context.Database.BeginTransaction(IsolationLevel.Serializable);
            var saved = context.CatalogSubmissions
                .FromSqlRaw("SELECT * FROM [deploy].[CatalogSubmission] WITH (UPDLOCK, HOLDLOCK)")
                .SingleOrDefault(row => row.CallerHash == callerHash && row.SubmissionKey == submission.Key);
            if (saved != null)
            {
                VerifySubmissionPayload(saved, submission);
                transaction.Commit();
                return saved.DeploymentRequestId;
            }
            // A failed/unknown commit is retried by checking the durable key first.
            request.Id = 0;
            var definitions = CaptureDefinitions(context, request);
            var snapshots = CaptureCatalogInputs(context, request);
            request.IsProd = context.Environments.Any(environment =>
                environment.Name == request.Environment && environment.IsProd);
            context.DeploymentRequests.Add(request);
            context.SaveChanges();
            foreach (var definition in definitions)
            {
                definition.DeploymentRequestId = request.Id;
                context.DeploymentRequestDefinitions.Add(definition);
            }
            foreach (var snapshot in snapshots)
            {
                snapshot.DeploymentRequestId = request.Id;
                context.CatalogRequestInputs.Add(snapshot);
            }
            var salt = RandomNumberGenerator.GetBytes(32);
            context.CatalogSubmissions.Add(new CatalogSubmission
            {
                CallerHash = callerHash,
                SubmissionKey = submission.Key,
                PayloadSalt = salt,
                PayloadFingerprint = HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(submission.CanonicalPayload)),
                DeploymentRequestId = request.Id,
                ComponentJson = submission.ComponentJson
            });
            context.SaveChanges();
            transaction.Commit();
            return request.Id;
        });
    }
}
