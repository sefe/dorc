using System.Collections;

namespace Dorc.Core.Interfaces;

public interface IEnvBackups
{
    List<string> GetSnapsOfStatus(string stagingInstance, string status);
}