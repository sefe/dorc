using Dorc.ApiModel;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces;

public interface IBundledRequestsPersistentSource
{
    IEnumerable<BundledRequestsApiModel> GetRequestsForBundle(string bundleName);
    IEnumerable<BundledRequestsApiModel> GetBundles(string projectName);
}