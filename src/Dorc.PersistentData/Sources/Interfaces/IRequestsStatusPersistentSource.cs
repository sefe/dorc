using Dorc.ApiModel;
using System.Security.Principal;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IRequestsStatusPersistentSource
    {
        GetRequestStatusesListResponseDto GetRequestStatusesByPage(int limit, int page, PagedDataOperators operators,
            IPrincipal principal);

        void AppendLogToJob(int deploymentResultId, string log);
        void SetUncLogPathforRequest(int requestId, string uncLogPath);
    }
}