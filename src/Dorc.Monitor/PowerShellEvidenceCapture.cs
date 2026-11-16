using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dorc.ApiModel;
using Dorc.Monitor.Security;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Monitor;

internal sealed class PowerShellEvidenceCapture : IPowerShellEvidenceCapture
{
    public void Prepare(ScriptGroup group, string revision, int requestId, int offset, string accountName)
    {
        if (string.IsNullOrWhiteSpace(revision)) throw new InvalidOperationException("A pinned script revision is required.");
        var root = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
            "DOrc", "PowerShellEvidence");
        RestrictedDirectory.Ensure(root);
        var directory = Path.Combine(root, $"{group.DeployResultId}-{Guid.NewGuid():N}");
        RestrictedDirectory.Ensure(directory, new NTAccount(accountName));
        group.DefinitionRequestId = requestId;
        group.DefinitionRevision = revision;
        group.EvidenceOrdinalOffset = offset;
        group.PowerShellEvidenceDirectory = directory;
        for (var index = 0; index < group.ScriptProperties.Count; index++)
        {
            var ordinal = offset + index;
            var evidence = new PowerShellEvidence
            {
                RequestId = requestId, ResultId = group.DeployResultId, DefinitionRevision = revision,
                ScriptOrdinal = ordinal, Phase = "Allocated", RequestedRuntime = group.PowerShellVersionNumber,
                SourcePath = group.ScriptProperties[index].ScriptPath, BuildIdentity = group.SourceBuildIdentity
            };
            using var stream = new FileStream(Path.Combine(directory, ordinal + ".json"), FileMode.CreateNew, FileAccess.Write);
            JsonSerializer.Serialize(stream, evidence);
            stream.Flush(true);
        }
    }

    public bool Ingest(ScriptGroup group, IRequestsPersistentSource requests)
    {
        var complete = true;
        var durable = true;
        for (var index = 0; index < group.ScriptProperties.Count; index++)
        {
            var ordinal = group.EvidenceOrdinalOffset + index;
            var path = Path.Combine(group.PowerShellEvidenceDirectory, ordinal + ".json");
            var info = new FileInfo(path);
            PowerShellEvidence? evidence = null;
            if (info.Exists && info.Length <= PowerShellInput.MaximumEvidenceBytes &&
                (info.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                try { evidence = PowerShellInput.ReadEvidence(path); }
                catch (JsonException) { }
                catch (InvalidDataException) { }
            }
            if (evidence == null || evidence.SchemaVersion != 1 ||
                evidence.RequestId != group.DefinitionRequestId || evidence.ResultId != group.DeployResultId ||
                evidence.ScriptOrdinal != ordinal || evidence.DefinitionRevision != group.DefinitionRevision ||
                evidence.RuntimeVersion?.Length > 64 ||
                evidence.RequestedRuntime?.Length > 64 ||
                evidence.SourcePath?.Length > 2048 || evidence.BuildIdentity?.Length > 256 ||
                evidence.RequestedRuntime != group.PowerShellVersionNumber ||
                evidence.SourcePath != group.ScriptProperties[index].ScriptPath ||
                evidence.BuildIdentity != group.SourceBuildIdentity ||
                evidence.PreparedAt > DateTimeOffset.UtcNow.AddMinutes(5) ||
                evidence.InvocationObservedAt > DateTimeOffset.UtcNow.AddMinutes(5) ||
                evidence.CompletedAt > DateTimeOffset.UtcNow.AddMinutes(5) ||
                evidence.InvocationObservedAt < evidence.PreparedAt || evidence.CompletedAt < evidence.InvocationObservedAt ||
                (evidence.TextSha256 != null && !Regex.IsMatch(evidence.TextSha256, @"\A[A-F0-9]{64}\z")) ||
                (evidence.Phase != "Allocated" && (string.IsNullOrWhiteSpace(evidence.TextSha256) ||
                    string.IsNullOrWhiteSpace(evidence.RuntimeVersion) || !evidence.PreparedAt.HasValue)) ||
                ((evidence.Phase == "InvocationObserved" || evidence.Phase == "InvocationReturned") &&
                    !evidence.InvocationObservedAt.HasValue) ||
                (evidence.Phase == "InvocationReturned" && !evidence.CompletedAt.HasValue) ||
                (evidence.Phase == "Allocated" && (evidence.TextSha256 != null || evidence.PreparedAt.HasValue ||
                    evidence.InvocationObservedAt.HasValue || evidence.CompletedAt.HasValue)) ||
                (evidence.Phase != "Allocated" && evidence.Phase != "Prepared" &&
                 evidence.Phase != "InvocationObserved" && evidence.Phase != "InvocationReturned"))
            {
                evidence = new PowerShellEvidence
                {
                    RequestId = group.DefinitionRequestId, ResultId = group.DeployResultId, ScriptOrdinal = ordinal,
                    DefinitionRevision = group.DefinitionRevision, Phase = "Unknown"
                };
            }
            if (evidence.Phase == "Allocated")
            {
                evidence.Phase = "Unknown";
                evidence.PreparedAt = null;
            }
            complete &= evidence.Phase == "InvocationReturned" && evidence.CompletedAt.HasValue &&
                evidence.InvocationObservedAt.HasValue && evidence.PreparedAt != default;
            // A failed database write leaves the restricted file for recovery. It must
            // never be interpreted as a reason to invoke the workload again.
            try { requests.SavePowerShellEvidence(evidence); }
            catch { durable = false; throw; }
        }
        if (durable) Directory.Delete(group.PowerShellEvidenceDirectory, recursive: true);
        return complete;
    }
}
