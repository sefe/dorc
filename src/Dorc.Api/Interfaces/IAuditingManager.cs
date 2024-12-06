namespace Dorc.Api.Interfaces
{
    public interface IAuditingManager
    {

        int InsertAudit(long propertyId, long propertyValueId, string propertyName, string environmentName,
            string fromValue, string toValue, string updatedBy, string type);

    }
}