using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IPropertyValuesAuditPersistentSource
    {
        void AddRecord(long propertyId, long propertyValueId, string propertyName, string environmentName,
            string fromValue, string toValue, string updatedBy, string type);

        GetPropertyValuesAuditListResponseDto GetPropertyValueAuditsByPage(int limit, int page, PagedDataOperators operators);
    }
}