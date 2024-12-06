namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IDeploymentRequestProcessesPersistentSource
    {
        IEnumerable<int>? GetAssociatedRunnerProcessIds(int requestId);

        void AssociateProcessWithRequest(int processId, int requestId);

        void RemoveProcess(int processId);
    }
}
