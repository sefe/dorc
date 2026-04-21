using System.Runtime.Versioning;
using System.Security.Principal;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    [SupportedOSPlatform("windows")]
    public sealed class AccessControlController : ControllerBase
    {
        private readonly IAccessControlPersistentSource _accessControlPersistentSource;
        private readonly IActiveDirectorySearcher _adSearcher;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;

        public AccessControlController(IAccessControlPersistentSource accessControlPersistentSource,
            IActiveDirectorySearcher adSearcher,
            ISecurityPrivilegesChecker securityPrivilegesChecker)
        {
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _adSearcher = adSearcher;
            _accessControlPersistentSource = accessControlPersistentSource;
        }

        /// <summary>
        ///     Get the logs for the specified request ID
        /// </summary>
        /// <param name="accessControlType"></param>
        /// <param name="accessControlName"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(AccessSecureApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [HttpGet]
        public IActionResult Get(AccessControlType accessControlType, string accessControlName)
        {
            var secureObject = accessControlType == AccessControlType.Environment
                ? _accessControlPersistentSource.GetSecurableObjects<Environment>(User, accessControlName).FirstOrDefault()
                : _accessControlPersistentSource.GetSecurableObjects<Project>(User, accessControlName).FirstOrDefault();
            if (secureObject == null)
                return StatusCode(StatusCodes.Status400BadRequest,
                    "Unable to locate access control element!");

            var output = GetAccessSecureObjects(accessControlType, accessControlName, secureObject.ObjectId);

            output.UserEditable = accessControlType == AccessControlType.Environment &&
                                   _securityPrivilegesChecker.CanModifyEnvironment(User, accessControlName) ||
                                  accessControlType == AccessControlType.Project &&
                                   _securityPrivilegesChecker.CanModifyProject(User, accessControlName);

            output.UserIsOwner = accessControlType == AccessControlType.Environment &&
                                  _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, accessControlName) ||
                                 accessControlType == AccessControlType.Project &&
                                  _securityPrivilegesChecker.IsProjectOwnerOrAdmin(User, accessControlName);

            return StatusCode(StatusCodes.Status200OK, output);
        }

        private AccessSecureApiModel GetAccessSecureObjects(AccessControlType accessControlType, string accessControlName,
            Guid secureObject)
        {
            var accessControls = _accessControlPersistentSource.GetAccessControls(secureObject);

            var output = new AccessSecureApiModel
            {
                Name = accessControlName,
                ObjectId = secureObject,
                Type = accessControlType,
                Privileges = accessControls
            };
            return output;
        }

        /// <summary>
        /// Search users or groups in identity provider
        /// </summary>
        /// <param name="search"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<UserElementApiModel>))]
        [Route("SearchUsers")]
        [HttpGet]
        public IActionResult SearchUsers(string search)
        {
            var results = _adSearcher.Search(search);

            return StatusCode(StatusCodes.Status200OK, results);
        }

        /// <summary>
        /// Update an Access control
        /// </summary>
        /// <param name="accessControl"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(AccessSecureApiModel))]
        [HttpPut]
        public IActionResult Put(AccessSecureApiModel accessControl)
        {
            if (!HasModifyPermission(accessControl))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    "You are not authorized to edit access controls!");
            }

            if (accessControl.Type == AccessControlType.Environment &&
                IsRemovingAllOwners(accessControl))
            {
                return StatusCode(StatusCodes.Status400BadRequest,
                    "Cannot remove all owners from an environment. At least one owner must remain.");
            }

            SyncAccessControls(accessControl);

            var output = GetAccessSecureObjects(accessControl.Type, accessControl.Name, accessControl.ObjectId);
            return StatusCode(StatusCodes.Status200OK, output);
        }

        private bool HasModifyPermission(AccessSecureApiModel accessControl)
        {
            return accessControl.Type == AccessControlType.Environment &&
                    _securityPrivilegesChecker.CanModifyEnvironment(User, accessControl.Name) ||
                   accessControl.Type == AccessControlType.Project &&
                    _securityPrivilegesChecker.CanModifyProject(User, accessControl.Name);
        }

        private bool IsRemovingAllOwners(AccessSecureApiModel accessControl)
        {
            const int ownerFlag = 4;
            var currentOwners = _accessControlPersistentSource.GetAccessControls(accessControl.ObjectId)
                .Where(p => (p.Allow & ownerFlag) != 0)
                .ToList();

            var newOwners = accessControl.Privileges
                .Where(p => (p.Allow & ownerFlag) != 0)
                .ToList();

            return currentOwners.Any() && !newOwners.Any();
        }

        private void SyncAccessControls(AccessSecureApiModel accessControl)
        {
            var existingIds = _accessControlPersistentSource.GetAccessControls(accessControl.ObjectId)
                .Select(p => p.Id).ToArray();
            var newIds = accessControl.Privileges.Select(p => p.Id).ToArray();

            foreach (var existingId in existingIds)
            {
                if (!newIds.Contains(existingId))
                {
                    _accessControlPersistentSource.DeleteAccessControl(existingId, accessControl.ObjectId, User);
                }
            }

            foreach (var privilege in accessControl.Privileges)
            {
                if (privilege.Id == 0)
                {
                    _accessControlPersistentSource.AddAccessControl(privilege, accessControl.ObjectId, User);
                }
                else if (privilege.Id > 0)
                {
                    _accessControlPersistentSource.UpdateAccessControl(privilege, accessControl.ObjectId, User);
                }
            }
        }
    }
}
