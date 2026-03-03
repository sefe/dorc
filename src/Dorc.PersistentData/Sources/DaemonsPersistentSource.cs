using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{
    public sealed class DaemonsPersistentSource : IDaemonsPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public DaemonsPersistentSource(IDeploymentContextFactory contextFactory) =>
            _contextFactory = contextFactory;

        public IEnumerable<DaemonApiModel> GetDaemonsForServer(int serverId)
        {
            IEnumerable<DaemonApiModel> result;
            using (var context = _contextFactory.GetContext())
            {
                var server = GetServer(serverId, context);
                result = server != null
                    ? server.Services.Select(daemon => Map(daemon, server)).ToList()
                    : new List<DaemonApiModel>();
            }
            return result;
        }

        private static Server GetServer(int serverId, IDeploymentContext context) => context
                            .Servers
                            .Include(d => d.Services)
                            .FirstOrDefault(s => s.Id == serverId);

        public IEnumerable<DaemonApiModel> GetDaemons()
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.Services.Select(Map).ToList();
            }
        }

        private DaemonApiModel Map(Daemon daemon) =>
            new DaemonApiModel
            {
                Id = daemon.Id,
                Name = daemon.Name,
                AccountName = daemon.AccountName,
                DisplayName = daemon.DisplayName,
                ServiceType = daemon.ServiceType
            };

        private DaemonApiModel Map(Daemon d, Server s) =>
            new DaemonApiModel
            {
                Id = d.Id,
                Name = d.Name,
                AccountName = d.AccountName,
                DisplayName = d.DisplayName,
                ServerName = s.Name,
                ServiceType = d.ServiceType
            };

        private Daemon Map(DaemonApiModel model) =>
            new Daemon
            {
                Id = model.Id,
                Name = model.Name,
                AccountName = model.AccountName,
                DisplayName = model.DisplayName,
                ServiceType = model.ServiceType
            };

        public DaemonApiModel Add(DaemonApiModel daemonApiModel)
        {
            DaemonApiModel result;
            using (var context = _contextFactory.GetContext())
            {
                var mapToDatabase = Map(daemonApiModel);

                context.Services.Add(mapToDatabase);
                context.SaveChanges();
                result = context
                    .Services
                    .Where(daemon => daemon.Name.Equals(daemonApiModel.Name)).AsEnumerable()
                    .Select(Map)
                    .First();
            }
            return result;
        }

        public bool Delete(int daemonApiModelId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var entity = context.Services.Find(daemonApiModelId);
                if (entity == null)
                {
                    return false;
                }

                context.Services.Remove(entity);
                context.SaveChanges();
                return true;
            }
        }

        public DaemonApiModel Update(DaemonApiModel model)
        {
            DaemonApiModel result = null;
            using (var context = _contextFactory.GetContext())
            {
                var updatedDaemon = Map(model);

                var existingDaemon = context.Services
                    .FirstOrDefault(daemon => daemon.Id == model.Id);

                if (existingDaemon != null)
                {
                    existingDaemon.ServiceType = updatedDaemon.ServiceType;
                    existingDaemon.AccountName = updatedDaemon.AccountName;
                    existingDaemon.DisplayName = updatedDaemon.DisplayName;

                    context.SaveChanges();
                    result = context
                        .Services
                        .Where(d => d.Id == updatedDaemon.Id).AsEnumerable()
                        .Select(Map)
                        .First();
                }
            }
            return result;
        }

        public bool AttachDaemonToServer(int serverId, int daemonId)
        {
            using var context = _contextFactory.GetContext();
            var server = context.Servers
                .Include(s => s.Services)
                .FirstOrDefault(s => s.Id == serverId);
            if (server == null) return false;

            var daemon = context.Services.Find(daemonId);
            if (daemon == null) return false;

            if (server.Services.Any(d => d.Id == daemonId)) return true;

            server.Services.Add(daemon);
            context.SaveChanges();
            return true;
        }

        public bool DetachDaemonFromServer(int serverId, int daemonId)
        {
            using var context = _contextFactory.GetContext();
            var server = context.Servers
                .Include(s => s.Services)
                .FirstOrDefault(s => s.Id == serverId);
            if (server == null) return false;

            var daemon = server.Services.FirstOrDefault(d => d.Id == daemonId);
            if (daemon == null) return false;

            server.Services.Remove(daemon);
            context.SaveChanges();
            return true;
        }

        public void DiscoverAndMapDaemonsForServer(int serverId, IEnumerable<string> confirmedServiceNames)
        {
            using var context = _contextFactory.GetContext();
            var server = context.Servers
                .Include(s => s.Services)
                .FirstOrDefault(s => s.Id == serverId);
            if (server == null) return;

            var daemonsByName = context.Services
                .Where(d => confirmedServiceNames.Contains(d.Name))
                .ToList();

            foreach (var daemon in daemonsByName)
            {
                if (!server.Services.Any(s => s.Id == daemon.Id))
                    server.Services.Add(daemon);
            }

            context.SaveChanges();
        }
    }
}