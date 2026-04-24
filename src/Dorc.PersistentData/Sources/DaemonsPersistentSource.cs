using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Exceptions;
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
                    ? server.Daemons.Select(daemon => Map(daemon, server)).ToList()
                    : new List<DaemonApiModel>();
            }
            return result;
        }

        private static Server GetServer(int serverId, IDeploymentContext context) => context
                            .Servers
                            .Include(d => d.Daemons)
                            .FirstOrDefault(s => s.Id == serverId);

        public IEnumerable<DaemonApiModel> GetDaemons()
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.Daemons.Select(Map).ToList();
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
            using (var context = _contextFactory.GetContext())
            {
                if (context.Daemons.Any(d => d.Name == daemonApiModel.Name))
                {
                    throw new DaemonDuplicateException(
                        $"A daemon with Name '{daemonApiModel.Name}' already exists");
                }

                if (!string.IsNullOrEmpty(daemonApiModel.DisplayName)
                    && context.Daemons.Any(d => d.DisplayName == daemonApiModel.DisplayName))
                {
                    throw new DaemonDuplicateException(
                        $"A daemon with DisplayName '{daemonApiModel.DisplayName}' already exists");
                }

                var mapToDatabase = Map(daemonApiModel);
                context.Daemons.Add(mapToDatabase);
                context.SaveChanges();

                return Map(mapToDatabase);
            }
        }

        public bool Delete(int daemonApiModelId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var entity = context.Daemons
                    .Include(d => d.Server)
                    .FirstOrDefault(d => d.Id == daemonApiModelId);
                if (entity == null)
                {
                    return false;
                }

                entity.Server.Clear();
                context.Daemons.Remove(entity);
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

                var existingDaemon = context.Daemons
                    .FirstOrDefault(daemon => daemon.Id == model.Id);

                if (existingDaemon != null)
                {
                    existingDaemon.Name = updatedDaemon.Name;
                    existingDaemon.ServiceType = updatedDaemon.ServiceType;
                    existingDaemon.AccountName = updatedDaemon.AccountName;
                    existingDaemon.DisplayName = updatedDaemon.DisplayName;

                    context.SaveChanges();
                    result = context
                        .Daemons
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
                .Include(s => s.Daemons)
                .FirstOrDefault(s => s.Id == serverId);
            if (server == null) return false;

            var daemon = context.Daemons.Find(daemonId);
            if (daemon == null) return false;

            if (server.Daemons.Any(d => d.Id == daemonId)) return true;

            server.Daemons.Add(daemon);
            context.SaveChanges();
            return true;
        }

        public bool DetachDaemonFromServer(int serverId, int daemonId)
        {
            using var context = _contextFactory.GetContext();
            var server = context.Servers
                .Include(s => s.Daemons)
                .FirstOrDefault(s => s.Id == serverId);
            if (server == null) return false;

            var daemon = server.Daemons.FirstOrDefault(d => d.Id == daemonId);
            if (daemon == null) return false;

            server.Daemons.Remove(daemon);
            context.SaveChanges();
            return true;
        }
    }
}