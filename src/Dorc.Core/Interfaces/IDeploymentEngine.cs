namespace Dorc.Core.Interfaces
{
    public interface IDeploymentEngine
    {
        void Deploy(int deploymentRequestId, string scriptRoot);
    }
}