using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dorc.ApiModel
{
    public sealed class PowerShellEvidence
    {
        public int SchemaVersion { get; set; } = 1;
        public int RequestId { get; set; }
        public int ResultId { get; set; }
        public int ScriptOrdinal { get; set; }
        public string DefinitionRevision { get; set; }
        public string TextSha256 { get; set; }
        public string RuntimeVersion { get; set; }
        public string RequestedRuntime { get; set; }
        public string SourcePath { get; set; }
        public string BuildIdentity { get; set; }
        public string Phase { get; set; }
        public DateTimeOffset? PreparedAt { get; set; }
        public DateTimeOffset? InvocationObservedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }

    /// <summary>
    /// SHA256 of UTF8(script text), not raw file bytes. File.ReadAllText decodes
    /// BOMs; its returned string's newlines are preserved and passed to AddScript.
    /// The digest makes no claim about imports, modules or external data.
    /// </summary>
    public sealed class PowerShellInput
    {
        public const int MaximumEvidenceBytes = 8192;
        private readonly string evidencePath;
        private readonly PowerShellEvidence evidence;
        public string Text { get; private set; }
        public string TextSha256 { get { return evidence.TextSha256; } }

        public static string Fingerprint(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }

        public PowerShellInput(string scriptPath, string evidenceDirectory, int requestId,
            int resultId, string revision, int ordinal, string requestedRuntime, string actualRuntime)
        {
            if (string.IsNullOrEmpty(evidenceDirectory) || string.IsNullOrEmpty(revision))
                throw new InvalidOperationException("Monitor-prepared execution evidence is required.");
            evidencePath = Path.Combine(evidenceDirectory, ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json");
            var info = new FileInfo(evidencePath);
            if (!info.Exists || info.Length > MaximumEvidenceBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The evidence contract is missing or invalid.");
            evidence = ReadEvidence(evidencePath);
            if (evidence == null || evidence.SchemaVersion != 1 || evidence.RequestId != requestId ||
                evidence.ResultId != resultId || evidence.DefinitionRevision != revision ||
                evidence.ScriptOrdinal != ordinal || evidence.Phase != "Allocated" ||
                !string.Equals(evidence.SourcePath, scriptPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The evidence identity does not match this invocation.");
            Text = File.ReadAllText(scriptPath);
            evidence.TextSha256 = Fingerprint(Text);
            evidence.RequestedRuntime = requestedRuntime;
            evidence.RuntimeVersion = actualRuntime;
            evidence.PreparedAt = DateTimeOffset.UtcNow;
            evidence.Phase = "Prepared";
            Persist();
        }

        public void InvocationObserved()
        {
            evidence.InvocationObservedAt = DateTimeOffset.UtcNow;
            evidence.Phase = "InvocationObserved";
            Persist();
        }

        public static PowerShellEvidence ReadEvidence(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var buffer = new byte[MaximumEvidenceBytes + 1];
                var count = 0;
                while (count < buffer.Length)
                {
                    var read = stream.Read(buffer, count, buffer.Length - count);
                    if (read == 0) break;
                    count += read;
                }
                if (count > MaximumEvidenceBytes) throw new InvalidDataException("Execution evidence exceeds its size limit.");
                return JsonSerializer.Deserialize<PowerShellEvidence>(Encoding.UTF8.GetString(buffer, 0, count));
            }
        }

        public void Completed()
        {
            evidence.CompletedAt = DateTimeOffset.UtcNow;
            evidence.Phase = "InvocationReturned";
            Persist();
        }

        private void Persist()
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evidence));
            if (bytes.Length > MaximumEvidenceBytes) throw new InvalidOperationException("Execution evidence exceeds its size limit.");
            using (var stream = new FileStream(evidencePath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(0);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }
    }
}
