using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataGroupsController : ControllerBase
    {
        private readonly IAdGroupPersistentSource _adGroupPersistentSource;

        /// <summary>
        ///     Controller constructor
        /// </summary>
        /// <param name="adGroupPersistentSource"></param>
        /// <param name="envRepository"></param>
        public RefDataGroupsController(IAdGroupPersistentSource adGroupPersistentSource)
        {
            _adGroupPersistentSource = adGroupPersistentSource;
        }

        /// <summary>
        ///     Returns list of groups
        /// </summary>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<GroupApiModel>))]
        [HttpGet]
        public IActionResult Get()
        {
            var result = _adGroupPersistentSource.GetAdGroups();
            return StatusCode(StatusCodes.Status200OK, result);
        }

        /// <summary>
        ///     Returns Group by name
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GroupApiModel))]
        [HttpGet("{name}")]

        public IActionResult Get(string name)
        {
            var group = _adGroupPersistentSource.GetAdGroup(name);
            var result = new GroupApiModel
            {
                GroupName = group.Name,
                GroupId = group.Id
            };
            return StatusCode(StatusCodes.Status200OK, result);
        }
    }
}