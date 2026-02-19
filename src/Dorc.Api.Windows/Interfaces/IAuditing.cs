namespace Dorc.Api.Windows.Interfaces
{
    public interface IAuditing
    {

        int InsertAudit(long propertyId, long propertyValueId, string propertyName, string environmentName,
            string fromValue, string toValue, string updatedBy, string type);

    }
}