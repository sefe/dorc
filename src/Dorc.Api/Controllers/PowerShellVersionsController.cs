using Dorc.ApiModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class PowerShellVersionsController : ControllerBase
    {
        /// <summary>
        /// Get available PowerShell versions
        /// </summary>
        /// <returns>List of available PowerShell versions</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<PowerShellVersionDto>))]
        [HttpGet]
        public IActionResult Get()
        {
            var versions = Enum.GetValues<PowerShellVersion>()
                .Select(v => new PowerShellVersionDto
                {
                    Value = v.ToVersionString(),
                    DisplayName = v.ToVersionString()
                })
                .ToList();

            return StatusCode(StatusCodes.Status200OK, versions);
        }
    }
}
