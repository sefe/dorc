using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.PersistentData.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    public enum AccountGranularity
    {
        NotSet = -1,
        UsersAndGroups = 0,
        Users = 1,
        Groups = 2
    }

    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataUsersController : ControllerBase
    {
        private readonly IManageUsers _apiServices;

        public RefDataUsersController(IManageUsers services)
        {
            _apiServices = services;
        }

        /// <summary>
        ///     Gets list of Users
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public IEnumerable<UserApiModel> Get()
        {
            var result = _apiServices.GetUsersList<UserApiModel>();
            return result;
        }

        /// <summary>
        ///     Gets users, groups or both
        /// </summary>
        /// <param name="granularity">0 - return users and groups, 1 - return users, 2 - return groups</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<UserApiModel>))]
        [HttpGet("ByGranularity")]
        public IEnumerable<UserApiModel> GetByGranularity(
            AccountGranularity granularity = AccountGranularity.NotSet)
        {
            return granularity == AccountGranularity.NotSet
                ? _apiServices.GetUsersList<UserApiModel>()
                : _apiServices.GetUsersList<UserApiModel>(granularity);
        }

        /// <summary>
        ///     Gets users, groups or both
        /// </summary>
        /// <param name="environmentId">Environment ID</param>
        /// <param name="userAccountType">User type</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<UserApiModel>))]
        [HttpGet("ByEnvironmentId")]
        public IEnumerable<UserApiModel> GetByEnvironmentId(
            int environmentId,
            UserAccountType userAccountType)
        {
            return _apiServices.GetUsersForEnvironment(environmentId, userAccountType);
        }

        /// <summary>
        ///     Get user by lan ID or Display Name
        /// </summary>
        /// <param name="userName">Display Name or Lan ID</param>
        /// <returns>Json object</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(UserApiModel))]
        [HttpGet("ByName")]
        public UserApiModel GetByName(string userName)
        {
            if (string.IsNullOrEmpty(userName)) return new UserApiModel();
            //try find user by display name
            var result = _apiServices.GetUsersList<UserApiModel>().FirstOrDefault(u => u.DisplayName.Equals(userName));
            if (result != null)
                return result;
            //try to find user by lan id
            result = _apiServices.GetUsersList<UserApiModel>().FirstOrDefault(u => u.LanId.Equals(userName));

            return result ?? new UserApiModel();
        }

        /// <summary>
        ///     Create new user
        /// </summary>
        /// <param name="value">UserApiModel json model</param>
        /// <returns>Return UserApiModel model</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(UserApiModel))]
        [HttpPost]
        public UserApiModel Post([FromBody] UserApiModel value)
        {
            var user = _apiServices.AddUser(value);
            return user;
        }
    }
}