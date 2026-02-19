using Dorc.Api.Interfaces;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Services
{
    public class Auditing : IAuditing
    {
        private readonly IPropertyValuesAuditPersistentSource _propertyValuesAuditPersistentSource;
        private readonly IDeploymentContextFactory _contextFactory;

        public Auditing(IPropertyValuesAuditPersistentSource propertyValuesAuditPersistentSource, IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
            _propertyValuesAuditPersistentSource = propertyValuesAuditPersistentSource;
        }

        public int InsertAudit(long propertyId, long propertyValueId, string propertyName, string environmentName,
            string fromValue, string toValue, string updatedBy, string type)
        {
            if (fromValue == null) fromValue = string.Empty;
            if (toValue == null) toValue = string.Empty;

            _propertyValuesAuditPersistentSource.AddRecord(propertyId, propertyValueId, propertyName, environmentName, fromValue, toValue, updatedBy, type);

            return 1;
        }
    }
}