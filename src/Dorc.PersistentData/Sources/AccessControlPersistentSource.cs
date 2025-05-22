using System.Collections;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using System.Security.Claims;

namespace Dorc.PersistentData.Sources
{
    public class AccessControlPersistentSource : IAccessControlPersistentSource
    {
        private readonly IDeploymentContextFactory contextFactory;
        private readonly ILog logger;

        public AccessControlPersistentSource(
            IDeploymentContextFactory contextFactory,
            ILog logger)
        {
            this.contextFactory = contextFactory;
            this.logger = logger;
        }

        public IEnumerable<AccessControlApiModel> GetAccessControls()
        {
            using (var context = contextFactory.GetContext())
            {
                return context.AccessControls.Select(MapToAccessControlApiModel).ToList();
            }
        }

        public IEnumerable<AccessControlApiModel> GetAccessControls(Guid objectId)
        {
            using (var context = contextFactory.GetContext())
            {
                return context.AccessControls.Where(ac => ac.ObjectId == objectId).ToList().Select(MapToAccessControlApiModel);
            }
        }

        public AccessControlApiModel AddAccessControl(AccessControlApiModel accessControl, Guid objectId)
        {
            using (var context = contextFactory.GetContext())
            {
                var newAccessControl = context.AccessControls.Add(MapToAccessControl(accessControl, objectId));
                context.SaveChanges();

                return MapToAccessControlApiModel(newAccessControl.Entity);
            }
        }

        public AccessControlApiModel UpdateAccessControl(AccessControlApiModel accessControl)
        {
            using (var context = contextFactory.GetContext())
            {
                var existingAccessControl = context.AccessControls.Find(accessControl.Id);

                if (existingAccessControl == null) return null;

                existingAccessControl.Pid = accessControl.Pid;
                existingAccessControl.Name = accessControl.Name;
                existingAccessControl.Allow = accessControl.Allow;
                existingAccessControl.Deny = accessControl.Deny;

                context.SaveChanges();

                return MapToAccessControlApiModel(context.AccessControls.Find(accessControl.Id));
            }
        }

        public Guid DeleteAccessControl(int id)
        {
            using (var context = contextFactory.GetContext())
            {
                var existingAccessControl = context.AccessControls.Find(id);

                if (existingAccessControl == null) return Guid.Empty;

                var deletedGuid = existingAccessControl.ObjectId;
                context.AccessControls.Remove(existingAccessControl);

                context.SaveChanges();
                return deletedGuid;
            }
        }

        public IEnumerable<SecurityObject> GetSecurableObjects<TEntity>(ClaimsPrincipal user, string accessControlName) where TEntity : SecurityObject
        {
            using (var context = contextFactory.GetContext())
            {
                return ((IEnumerable)context.Set<TEntity>())
                    .Cast<SecurityObject>()
                    .OrderBy(x => x.Name)
                    .Where(x => x.Name == accessControlName).ToList();
            }
        }

        public IEnumerable<SecurityObject> GetSecurableObjects<TEntity>(Type type, ClaimsPrincipal user) where TEntity : SecurityObject
        {
            using (var context = contextFactory.GetContext())
            {
                return ((IEnumerable)context.Set<TEntity>())
                    .Cast<SecurityObject>()
                    .OrderBy(x => x.Name)
                    .ToList();
            }
        }

        private AccessControlApiModel MapToAccessControlApiModel(AccessControl ac)
        {
            return new AccessControlApiModel
            {
                Allow = ac.Allow,
                Deny = ac.Deny,
                Id = ac.Id,
                Name = ac.Name,
                Sid = ac.Sid,
                Pid = ac.Pid,
            };
        }


        private AccessControl MapToAccessControl(AccessControlApiModel ac, Guid objectId)
        {
            return new AccessControl
            {
                Allow = ac.Allow,
                Deny = ac.Deny,
                Id = ac.Id,
                Name = ac.Name,
                ObjectId = objectId,
                Pid = ac.Pid,
            };
        }
    }
}
