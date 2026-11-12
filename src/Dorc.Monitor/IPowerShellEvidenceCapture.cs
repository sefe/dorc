using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Monitor;

public interface IPowerShellEvidenceCapture
{
    void Prepare(ScriptGroup group, string revision, int requestId, int offset, string accountName);
    bool Ingest(ScriptGroup group, IRequestsPersistentSource requests);
}
