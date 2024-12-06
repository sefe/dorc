using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class EnvironmentContentApiModel
    {
        public string EnvironmentName { set; get; }
        public string FileShare { set; get; }
        public string Description { set; get; }
        public IEnumerable<DatabaseApiModel> DbServers { set; get; }
        public IEnumerable<ServerApiModel> AppServers { set; get; }
        public IEnumerable<EnvironmentContentWindowsServicesApiModel> WindowsServices { set; get; }
        public IEnumerable<EnvironmentContentBuildsApiModel> Builds { set; get; }
        public IEnumerable<UserApiModel> EndurUsers { set; get; }
        public IEnumerable<UserApiModel> DelegatedUsers { set; get; }
        public IEnumerable<ProjectApiModel> MappedProjects { get; set; }
    }
}