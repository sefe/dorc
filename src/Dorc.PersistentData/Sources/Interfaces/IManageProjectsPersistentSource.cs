using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IManageProjectsPersistentSource
    {
        IList<ComponentApiModel> GetOrderedComponents(int projectId);
        void ValidateComponents(IList<ComponentApiModel> components, int projectId, HttpRequestType httpRequestType);

        void TraverseComponents(IEnumerable<ComponentApiModel> components, int? parentId, int projectId,
            Action<ComponentApiModel, int, int?> action);

        void UpdateComponent(ComponentApiModel apiComponent, int projectId, int? parentId);

        void CreateComponent(ComponentApiModel apiComponent, int projectId, int? parentId);

        void DeleteComponents(IList<ComponentApiModel> apiComponents, int projectId);

        void FlattenApiComponents(IEnumerable<ComponentApiModel> components,
            IList<ComponentApiModel> flattenedComponents);

        ReleaseInformationApiModel GetRequestDetails(int requestId);
        bool GetStatusOfRequest(int requestId);

        void InsertRefDataAudit(string username, HttpRequestType requestType, RefDataApiModel refDataApiModel);

        IList<ComponentApiModel> GetOrderedComponents(IEnumerable<string> components);
    }
}