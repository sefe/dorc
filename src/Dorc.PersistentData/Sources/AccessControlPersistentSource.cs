using System.Collections;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Principal;

namespace Dorc.PersistentData.Sources
{
    public class AccessControlPersistentSource : IAccessControlPersistentSource
    {
        private readonly IDeploymentContextFactory contextFactory;

        public AccessControlPersistentSource(
            IDeploymentContextFactory contextFactory)
        {
            this.contextFactory = contextFactory;
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

        public AccessControlApiModel AddAccessControl(AccessControlApiModel accessControl, Guid objectId, ClaimsPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                var newAccessControl = context.AccessControls.Add(MapToAccessControl(accessControl, objectId));
                context.SaveChanges();

                var environment = context.Environments.FirstOrDefault(e => e.ObjectId == objectId);
                if (environment != null)
                {
                    AddAccessControlHistory(
                        environment,
                        "New User",
                        $"User: {accessControl.Name}, Permissions: {GetPermissionsString(accessControl.Allow)}",
                        user.Identity?.Name ?? "Unknown",
                        "Access Control - User Added",
                        context
                    );
                    context.SaveChanges();
                }

                return MapToAccessControlApiModel(newAccessControl.Entity);
            }
        }

        public AccessControlApiModel UpdateAccessControl(AccessControlApiModel accessControl, Guid objectId, ClaimsPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                var existingAccessControl = context.AccessControls.Find(accessControl.Id);

                if (existingAccessControl == null) return null;

                var oldPermissions = existingAccessControl.Allow;
                var oldName = existingAccessControl.Name;

                existingAccessControl.Pid = accessControl.Pid;
                existingAccessControl.Sid = accessControl.Sid;
                existingAccessControl.Name = accessControl.Name;
                existingAccessControl.Allow = accessControl.Allow;
                existingAccessControl.Deny = accessControl.Deny;

                context.SaveChanges();

                if (oldPermissions != accessControl.Allow)
                {
                    var environment = context.Environments.FirstOrDefault(e => e.ObjectId == objectId);
                    if (environment != null)
                    {
                        AddAccessControlHistory(
                            environment,
                            $"User: {oldName}, Permissions: {GetPermissionsString(oldPermissions)}",
                            $"User: {accessControl.Name}, Permissions: {GetPermissionsString(accessControl.Allow)}",
                            user.Identity?.Name ?? "Unknown",
                            "Access Control - Permissions Modified",
                            context
                        );
                        context.SaveChanges();
                    }
                }

                return MapToAccessControlApiModel(context.AccessControls.Find(accessControl.Id));
            }
        }

        public Guid DeleteAccessControl(int id, Guid objectId, ClaimsPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                var existingAccessControl = context.AccessControls.Find(id);

                if (existingAccessControl == null) return Guid.Empty;

                var deletedGuid = existingAccessControl.ObjectId;

                var deletedName = existingAccessControl.Name;
                var deletedPermissions = existingAccessControl.Allow;

                context.AccessControls.Remove(existingAccessControl);

                context.SaveChanges();

                var environment = context.Environments.FirstOrDefault(e => e.ObjectId == objectId);
                if (environment != null)
                {
                    AddAccessControlHistory(
                        environment,
                        $"User: {deletedName}, Permissions: {GetPermissionsString(deletedPermissions)}",
                        "User Removed",
                        user.Identity?.Name ?? "Unknown",
                        "Access Control - User Removed",
                        context
                    );
                    context.SaveChanges();
                }

                return deletedGuid;
            }
        }

        private void AddAccessControlHistory(
            Model.Environment environment,
            string fromValue,
            string toValue,
            string updatedBy,
            string updateType,
            IDeploymentContext context)
        {
            var newHistory = new EnvironmentHistory
            {
                Environment = environment,
                UpdateDate = DateTime.Now,
                UpdateType = updateType,
                UpdatedBy = updatedBy,
                FromValue = fromValue,
                ToValue = toValue,
                Details = String.Empty
            };
            context.EnvironmentHistories.Add(newHistory);

            environment.LastUpdate = newHistory.UpdateDate;
        }

        private string GetPermissionsString(int permissions)
        {
            var perms = new List<string>();
            if ((permissions & (int)AccessLevel.Write) != 0) perms.Add("Write");
            if ((permissions & (int)AccessLevel.ReadSecrets) != 0) perms.Add("Read Secrets");
            if ((permissions & (int)AccessLevel.Owner) != 0) perms.Add("Owner");
            return perms.Any() ? string.Join(", ", perms) : "None";
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
                Sid = ac.Sid,
            };
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
    }
}
