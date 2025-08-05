using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.PersistentData.Sources
{
    public class PermissionsPersistentSource : IPermissionsPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public PermissionsPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public IEnumerable<PermissionDto> GetAllPermissions()
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.Permissions.OrderBy(p => p.DisplayName).Select(MapToPermssionDto).ToList();
            }
        }

        public PermissionDto GetPermissions(int userId)
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.Permissions.Where(u => u.Id == userId).OrderBy(p => p.DisplayName).Select(MapToPermssionDto).First();
            }
        }

        public PermissionDto UpdatePermission(int id, PermissionDto perm)
        {
            using (var context = _contextFactory.GetContext())
            {
                if (!context.Permissions.Any(u => u.DisplayName == perm.DisplayName && u.Id != id))
                {
                    var found = context.Permissions.First(u => u.Id == id);
                    found.DisplayName = perm.DisplayName;
                    found.Name = perm.PermissionName;
                    context.SaveChanges();
                }
                else
                {
                    throw new ArgumentException($"Permission with display name \"{perm.DisplayName}\" already exists");
                }

                return MapToPermssionDto(context.Permissions.First(u => u.Name == perm.PermissionName));
            }
        }

        public void DeletePermission(PermissionDto perm)
        {
            using (var context = _contextFactory.GetContext())
            {
                var found = context.Permissions.First(u => u.Name == perm.PermissionName);
                context.Permissions.Remove(found);
                context.SaveChanges();
            }
        }

        public void DeletePermission(int permId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var found = context.Permissions.First(u => u.Id == permId);
                context.Permissions.Remove(found);
                context.SaveChanges();
            }
        }

        public PermissionDto CreatePermission(PermissionDto perm)
        {
            using (var context = _contextFactory.GetContext())
            {
                if (!context.Permissions.Any(u => u.DisplayName == perm.DisplayName))
                {
                    context.Permissions.Add(MapToPermssion(perm));
                    context.SaveChanges();
                }
                else
                {
                    throw new ArgumentException($"Permission with display name \"{perm.DisplayName}\" already exists");
                }
                return MapToPermssionDto(context.Permissions.FirstOrDefault(u => u.Name == perm.PermissionName));
            }
        }

        private PermissionDto MapToPermssionDto(Permission perm)
        {
            return new PermissionDto
            {
                Id = perm.Id,
                DisplayName = perm.DisplayName,
                PermissionName = perm.Name
            };
        }

        private Permission MapToPermssion(PermissionDto perm)
        {
            return new Permission
            {
                Id = perm.Id,
                DisplayName = perm.DisplayName,
                Name = perm.PermissionName
            };
        }
    }
}