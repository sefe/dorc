using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataComponentsController : ControllerBase
    {
        private readonly IApiServices _apiServices;

        public RefDataComponentsController(IApiServices apiServices)
        {
            _apiServices = apiServices;
        }

        /// <summary>
        ///     Returns components for Project
        /// </summary>
        /// <param name="id">Project name</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(TemplateApiModel<ComponentApiModel>))]
        [HttpGet]
        public IActionResult Get(string id)
        {
            var components = _apiServices.GetComponentsByProject(id);
            return StatusCode(StatusCodes.Status200OK, components);
        }
    }
}