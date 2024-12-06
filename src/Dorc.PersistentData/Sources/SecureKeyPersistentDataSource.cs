using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.PersistentData.Sources
{
    public class SecureKeyPersistentDataSource : ISecureKeyPersistentDataSource
    {
        private readonly SecureKey _secureKey;

        public SecureKeyPersistentDataSource(IDeploymentContextFactory contextFactory)
        {
            using (var context = contextFactory.GetContext())
            {
                _secureKey = context.SecureKeys.First(s => s.Id == 1);
            }
        }

        public string GetInitialisationVector()
        {
            return _secureKey.IV;
        }

        public string GetSymmetricKey()
        {
            return _secureKey.Key;
        }
    }
}
