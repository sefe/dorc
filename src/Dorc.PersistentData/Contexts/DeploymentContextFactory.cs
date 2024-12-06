namespace Dorc.PersistentData.Contexts
{
    public class DeploymentContextFactory : IDeploymentContextFactory
    {
        private readonly string _cxnString;

        public DeploymentContextFactory(string cxnString)
        {
            _cxnString = cxnString;
        }

        public IDeploymentContext GetContext()
        {
            return new DeploymentContext(_cxnString);
        }
    }
}