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
    public sealed class ServerAuditController : ControllerBase
    {
        private readonly IServersAuditPersistentSource _serverAuditPersistentSource;

        public ServerAuditController(IServersAuditPersistentSource serverAuditPersistentSource)
        {
            _serverAuditPersistentSource = serverAuditPersistentSource;
        }

        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetServerAuditListResponseDto))]
        [HttpPut]
        public IActionResult Put([FromBody] PagedDataOperators operators, int page = 1, int limit = 50)
        {
            var result = _serverAuditPersistentSource.GetServerAudit(limit, page, operators);
            return StatusCode(StatusCodes.Status200OK, result);
        }
    }
}
