namespace Dorc.PersistentData.Contexts
{
    public interface IDeploymentContextFactory
    {
        IDeploymentContext GetContext();
    }
}