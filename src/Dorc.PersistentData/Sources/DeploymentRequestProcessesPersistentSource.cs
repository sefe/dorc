using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{
    public class DeploymentRequestProcessesPersistentSource : IDeploymentRequestProcessesPersistentSource
    {
        private readonly IDeploymentContextFactory contextFactory;

        public DeploymentRequestProcessesPersistentSource(IDeploymentContextFactory contextFactory)
        {
            this.contextFactory = contextFactory;
        }

        public IEnumerable<int>? GetAssociatedRunnerProcessIds(int requestId)
        {
            using (var context = contextFactory.GetContext())
            {
                return context.DeploymentRequestProcesses
                    .Include(process => process.DeploymentRequest)
                    .Where(process => process.DeploymentRequest.Id == requestId)
                    .Select(process => process.ProcessId)
                    .ToList();
            }
        }

        public void AssociateProcessWithRequest(int processId, int requestId)
        {
            using (var context = contextFactory.GetContext())
            {
                context.DeploymentRequestProcesses.Add(new DeploymentRequestProcess
                {
                    ProcessId = processId,
                    DeploymentRequest = context.DeploymentRequests.First(request => request.Id == requestId)
                });
                context.SaveChanges();
            }
        }

        public void RemoveProcess(int processId)
        {
            using (var context = contextFactory.GetContext())
            {
                context.DeploymentRequestProcesses
                    .Where(process => process.ProcessId == processId)
                    .ExecuteDelete();
            }
        }
    }
}
