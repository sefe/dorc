using Dorc.ApiModel;
using Dorc.Core;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;

namespace Dorc.Monitor.Tests.Data
{
    internal static class DeploymentRequestData
    {
        public static DeploymentRequest GetDeploymentRequest(string environmentName, string username, string? project = null)
        {
            var rnd = new Random(100);
            var serializer = new DeploymentRequestDetailSerializer();

            DeploymentRequestDetail requestDetail = new DeploymentRequestDetail()
            {
                EnvironmentName = environmentName,
                BuildDetail = new BuildDetail
                {
                    Project = project ?? "project_" + rnd.Next(100).ToString(),
                    BuildNumber = "bn_" + rnd.Next(100).ToString()
                },
                Components = new List<string>()
            };

            return new DeploymentRequest
            {
                RequestDetails = serializer.Serialize(requestDetail),
                UserName = username,
                RequestedTime = DateTimeOffset.Now,
                Project = requestDetail.BuildDetail.Project,
                Environment = requestDetail.EnvironmentName,
                BuildNumber = requestDetail.BuildDetail.BuildNumber,
                Components = string.Join("|", requestDetail.Components),
            };
        }

        public static DeploymentRequestApiModel GetPendingApiModel(string environmentName, string username, string? project = null)
        {
            return RequestsPersistentSource.MapToDeploymentRequestApiModel(
                GetDeploymentRequest(environmentName, username, project)
                );
        }

        public static DeploymentRequest WithStatus(this DeploymentRequest dr, DeploymentRequestStatus status)
        {
            dr.Status = status.ToString();
            return dr;
        }
    }
}
