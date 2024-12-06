﻿using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class DelegatedUsersController : ControllerBase
    {
        private readonly IUsersPersistentSource _usersPersistentSource;
        private readonly ISecurityPrivilegesChecker _securityService;
        private readonly ILog _logger;

        public DelegatedUsersController(IUsersPersistentSource usersPersistentSource, ISecurityPrivilegesChecker securityService, ILog logger)
        {
            _logger = logger;
            _securityService = securityService;
            _usersPersistentSource = usersPersistentSource;
        }

        /// <summary>
        ///    Get unallocated users
        /// </summary>
        /// <param name="envName"></param>
        /// <returns></returns>
        [HttpGet]
        [Produces(typeof(IEnumerable<UserApiModel>))]
        [Route("GetUnallocatedUsers")]
        public IActionResult GetUnallocatedUsers(string envName)
        {
            try
            {
                return Ok(_usersPersistentSource.GetUnallocatedUsers(envName));
            }
            catch (Exception e)
            {
                _logger.Error(e);
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        /// <summary>
        /// Delete delegated user
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="envName"></param>
        /// <returns></returns>
        [HttpDelete]
        [Produces(typeof(bool))]
        public IActionResult Delete(int userId, string envName)
        {
            try
            {

                return _securityService.IsEnvironmentOwnerOrAdmin(User, envName)
                    ? StatusCode(StatusCodes.Status200OK,
                        _usersPersistentSource.DeleteDelegatedUser(userId, envName, User))
                    : StatusCode(StatusCodes.Status403Forbidden, $"You are not authorized to edit {envName}");
            }
            catch (Exception e)
            {
                _logger.Error(e);
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        /// <summary>
        /// Create delegated user
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="envName"></param>
        /// <returns></returns>
        [HttpPost]
        [Produces(typeof(UserApiModel))]
        public IActionResult Post(int userId, string envName)
        {
            try
            {
                return _securityService.IsEnvironmentOwnerOrAdmin(User, envName)
                    ? StatusCode(StatusCodes.Status200OK,
                        _usersPersistentSource.AddDelegatedUser(userId, envName, User))
                    : StatusCode(StatusCodes.Status403Forbidden, $"You are not authorized to edit {envName}");
            }
            catch (Exception e)
            {
                _logger.Error(e);
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }
    }
}