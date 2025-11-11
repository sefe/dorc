using System.Collections;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using System.Linq;

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
                existingAccessControl.Sid = accessControl.Sid;
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

        public void UpdateAccessControlsWithHistory(
            Guid objectId,
            List<AccessControlApiModel> newPrivileges,
            ClaimsPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                // Get environment for history tracking
                var environment = context.Environments.FirstOrDefault(e => e.ObjectId == objectId);
                
                if (environment == null)
                    throw new ArgumentException("Environment not found");
                    
                // Get username from ClaimsPrincipal
                var username = user.Identity?.Name ?? "Unknown";
                
                // Get existing access controls
                var existingAccessControls = context.AccessControls
                    .Where(ac => ac.ObjectId == objectId)
                    .ToList();
                    
                var existingDict = existingAccessControls.ToDictionary(ac => ac.Id);
                var newIds = newPrivileges.Select(p => p.Id).ToArray();
                
                // Track removed users
                foreach (var existing in existingAccessControls)
                {
                    if (!newIds.Contains(existing.Id))
                    {
                        AddAccessControlHistory(
                            environment,
                            $"User: {existing.Name}, Permissions: {GetPermissionsString(existing.Allow)}",
                            "User Removed",
                            username,
                            "Access Control - User Removed",
                            context
                        );
                        
                        context.AccessControls.Remove(existing);
                    }
                }
                
                // Track added or modified users
                foreach (var privilege in newPrivileges)
                {
                    if (privilege.Id == 0)
                    {
                        // NEW USER
                        var newAccessControl = new AccessControl
                        {
                            ObjectId = objectId,
                            Name = privilege.Name,
                            Sid = privilege.Sid,
                            Pid = privilege.Pid,
                            Allow = privilege.Allow,
                            Deny = privilege.Deny
                        };
                        context.AccessControls.Add(newAccessControl);
                        
                        AddAccessControlHistory(
                            environment,
                            "New User",
                            $"User: {privilege.Name}, Permissions: {GetPermissionsString(privilege.Allow)}",
                            username,
                            "Access Control - User Added",
                            context
                        );
                    }
                    else if (existingDict.TryGetValue(privilege.Id, out var existing))
                    {
                        if (existing.Allow != privilege.Allow || existing.Deny != privilege.Deny)
                        {
                            AddAccessControlHistory(
                                environment,
                                $"User: {existing.Name}, Permissions: {GetPermissionsString(existing.Allow)}",
                                $"User: {privilege.Name}, Permissions: {GetPermissionsString(privilege.Allow)}",
                                username,
                                "Access Control - Permissions Modified",
                                context
                            );
                            
                            existing.Allow = privilege.Allow;
                            existing.Deny = privilege.Deny;
                            existing.Name = privilege.Name;
                            existing.Sid = privilege.Sid;
                            existing.Pid = privilege.Pid;
                        }
                    }
                }
                
                context.SaveChanges();
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
                Details = $"Access control change for environment: {environment.Name}"
            };
            context.EnvironmentHistories.Add(newHistory);
            
            environment.LastUpdate = newHistory.UpdateDate;
        }

        private string GetPermissionsString(int permissions)
        {
            var perms = new List<string>();
            if ((permissions & 1) != 0) perms.Add("Write");
            if ((permissions & 2) != 0) perms.Add("Read Secrets");
            if ((permissions & 4) != 0) perms.Add("Owner");
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
    }
}
