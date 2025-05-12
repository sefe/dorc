using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces;

public interface IBundledRequestsPersistentSource
{
    IEnumerable<BundledRequestsApiModel> GetRequestsForBundle(string bundleName);
    IEnumerable<BundledRequestsApiModel> GetBundles(string projectName);
    void AddRequestToBundle(BundledRequestsApiModel model);
    void UpdateRequestInBundle(BundledRequestsApiModel model); // Added missing method
    void DeleteRequestFromBundle(int id);
}
