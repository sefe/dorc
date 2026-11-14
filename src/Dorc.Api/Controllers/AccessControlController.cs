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
        private const int AC_ALLOW_READ_SECRETS = 2;
        private const int AC_ALLOW_OWNER = 4;

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

            output.UserCanReadSecrets = accessControlType == AccessControlType.Environment &&
                                        _securityPrivilegesChecker.CanGrantReadSecrets(User, accessControlName);

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
            if (accessControl.Type == AccessControlType.Environment &&
                 _securityPrivilegesChecker.CanModifyEnvironment(User, accessControl.Name) ||
                accessControl.Type == AccessControlType.Project &&
                 _securityPrivilegesChecker.CanModifyProject(User, accessControl.Name))
            {
                // Resolve the target object from its Name (which is what we authorized against)
                // and ignore any client-supplied ObjectId. Otherwise a caller could pass the Name
                // of an object they own together with the ObjectId of an object they do not, and
                // have the ACL writes land on the latter (IDOR / privilege escalation).
                var authorizedObject = accessControl.Type == AccessControlType.Environment
                    ? _accessControlPersistentSource.GetSecurableObjects<Environment>(User, accessControl.Name).FirstOrDefault()
                    : _accessControlPersistentSource.GetSecurableObjects<Project>(User, accessControl.Name).FirstOrDefault();
                if (authorizedObject == null)
                {
                    return StatusCode(StatusCodes.Status400BadRequest,
                        "Unable to locate access control element!");
                }
                accessControl.ObjectId = authorizedObject.ObjectId;

                var requestingUserIsOwner = accessControl.Type == AccessControlType.Environment
                    ? _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, accessControl.Name)
                    : _securityPrivilegesChecker.IsProjectOwnerOrAdmin(User, accessControl.Name);

                // Prevent users without the read-secrets grant capability from administering it.
                // This asks whether the caller may ADMINISTER the privilege (CanGrantReadSecrets),
                // which is distinct from exercising it (CanReadSecrets - restricted to service
                // principals); on projects, ReadSecrets may never be newly granted.
                var requestingUserCanReadSecrets = accessControl.Type == AccessControlType.Environment &&
                                                   _securityPrivilegesChecker.CanGrantReadSecrets(User, accessControl.Name);

                if (accessControl.Privileges == null)
                {
                    return StatusCode(StatusCodes.Status400BadRequest,
                        "Privileges list is required.");
                }

                var existingAccessControls = _accessControlPersistentSource.GetAccessControls(accessControl.ObjectId).ToList();
                var existingPrivileges = existingAccessControls.ToDictionary(p => p.Id);

                // Validate structural ID integrity first (negative IDs, duplicate positive IDs,
                // or IDs belonging to a different object).
                var seenPositiveIds = new HashSet<int>();
                foreach (var privilege in accessControl.Privileges)
                {
                    if (privilege.Id < 0)
                    {
                        return StatusCode(StatusCodes.Status400BadRequest,
                            "Invalid access control entry ID.");
                    }

                    if (privilege.Id > 0)
                    {
                        if (!seenPositiveIds.Add(privilege.Id))
                        {
                            return StatusCode(StatusCodes.Status400BadRequest,
                                $"Duplicate access control entry ID {privilege.Id} in request.");
                        }

                        if (!existingPrivileges.ContainsKey(privilege.Id))
                        {
                            return StatusCode(StatusCodes.Status400BadRequest,
                                $"Access control entry ID {privilege.Id} does not belong to '{accessControl.Name}'.");
                        }
                    }
                }

                var newIds = accessControl.Privileges.Where(p => p.Id > 0).Select(p => p.Id).ToHashSet();
                var deletedEntries = existingAccessControls.Where(e => !newIds.Contains(e.Id)).ToList();

                if (!requestingUserCanReadSecrets)
                {
                    foreach (var privilege in accessControl.Privileges)
                    {
                        var existing = privilege.Id > 0 ? existingPrivileges[privilege.Id] : null;
                        var hasReadSecrets = (privilege.Allow & AC_ALLOW_READ_SECRETS) != 0;
                        var hadReadSecretsBefore = existing != null && (existing.Allow & AC_ALLOW_READ_SECRETS) != 0;

                        var changesReadSecretsPrincipal = accessControl.Type == AccessControlType.Environment &&
                            hadReadSecretsBefore &&
                            (!string.Equals(privilege.Pid ?? string.Empty, existing!.Pid ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                             !string.Equals(privilege.Sid ?? string.Empty, existing.Sid ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                             !string.Equals(privilege.Name, existing.Name, StringComparison.Ordinal));

                        if (accessControl.Type == AccessControlType.Environment && (hasReadSecrets != hadReadSecretsBefore || changesReadSecretsPrincipal) ||
                            accessControl.Type == AccessControlType.Project && hasReadSecrets && !hadReadSecretsBefore)
                        {
                            return StatusCode(StatusCodes.Status403Forbidden,
                                "You do not have permission to grant or revoke Read Secrets access.");
                        }
                    }

                    if (accessControl.Type == AccessControlType.Environment &&
                        deletedEntries.Any(e => (e.Allow & AC_ALLOW_READ_SECRETS) != 0))
                    {
                        return StatusCode(StatusCodes.Status403Forbidden,
                            "You do not have permission to revoke Read Secrets access.");
                    }
                }

                // Reject in-place Sid/Pid mutations on any existing ACL row (even for authorized callers).
                foreach (var privilege in accessControl.Privileges.Where(p => p.Id > 0))
                {
                    var existingEntry = existingPrivileges[privilege.Id];
                    var existingSid = existingEntry.Sid ?? string.Empty;
                    var incomingSid = privilege.Sid ?? string.Empty;
                    var existingPid = existingEntry.Pid ?? string.Empty;
                    var incomingPid = privilege.Pid ?? string.Empty;

                    if (!string.Equals(existingSid, incomingSid, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingPid, incomingPid, StringComparison.OrdinalIgnoreCase))
                    {
                        return StatusCode(StatusCodes.Status400BadRequest,
                            "Changing the principal (Sid/Pid) of an existing access control entry is not allowed.");
                    }
                }

                // Only an Owner or Admin can add, modify, or remove Deny bits
                if (!requestingUserIsOwner)
                {
                    foreach (var privilege in accessControl.Privileges)
                    {
                        var previousDeny = privilege.Id > 0 ? existingPrivileges[privilege.Id].Deny : 0;
                        if (privilege.Deny != previousDeny)
                        {
                            return StatusCode(StatusCodes.Status403Forbidden,
                                "Only an Owner or Admin can grant or modify Deny access controls.");
                        }
                    }

                    if (deletedEntries.Any(e => e.Deny != 0))
                    {
                        return StatusCode(StatusCodes.Status403Forbidden,
                            "Only an Owner or Admin can remove Deny access controls.");
                    }
                }

                var currentOwners = existingAccessControls
                    .Where(p => (p.Allow & AC_ALLOW_OWNER) != 0)
                    .ToList();
                var newOwners = accessControl.Privileges
                    .Where(p => (p.Allow & AC_ALLOW_OWNER) != 0)
                    .ToList();

                // For environments, ensure at least one owner remains
                if (accessControl.Type == AccessControlType.Environment && currentOwners.Any() && !newOwners.Any())
                {
                    return StatusCode(StatusCodes.Status400BadRequest,
                        "Cannot remove all owners from an environment. At least one owner must remain.");
                }

                // Only an existing Owner or Admin may grant or revoke Owner access
                if (!requestingUserIsOwner)
                {
                    var currentOwnerIds = currentOwners.Select(o => o.Id).OrderBy(id => id).ToList();
                    var newOwnerIds = newOwners.Select(o => o.Id).OrderBy(id => id).ToList();
                    var hasNewUnpersistedOwner = newOwners.Any(o => o.Id <= 0);
                    if (hasNewUnpersistedOwner || !currentOwnerIds.SequenceEqual(newOwnerIds))
                    {
                        return StatusCode(StatusCodes.Status403Forbidden,
                            "Only an Owner or Admin can grant or revoke Owner access.");
                    }
                }

                foreach (var deletedEntry in deletedEntries)
                {
                    _accessControlPersistentSource.DeleteAccessControl(deletedEntry.Id, accessControl.ObjectId, User);
                }

                foreach (var accessControlPrivilege in accessControl.Privileges)
                {
                    if (accessControlPrivilege.Id == 0)
                    {
                        _accessControlPersistentSource.AddAccessControl(accessControlPrivilege, accessControl.ObjectId, User);
                    }
                    else if (accessControlPrivilege.Id > 0)
                    {
                        _accessControlPersistentSource.UpdateAccessControl(accessControlPrivilege, accessControl.ObjectId, User);
                    }
                }

                var output = GetAccessSecureObjects(accessControl.Type, accessControl.Name, accessControl.ObjectId);
                return StatusCode(StatusCodes.Status200OK, output);
            }
            else
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    "You are not authorized to edit access controls!");
            }
        }
    }
}
