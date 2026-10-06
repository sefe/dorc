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
    public sealed class DatabaseAuditController : ControllerBase
    {
        private readonly IDatabasesAuditPersistentSource _databaseAuditPersistentSource;

        public DatabaseAuditController(IDatabasesAuditPersistentSource databaseAuditPersistentSource)
        {
            _databaseAuditPersistentSource = databaseAuditPersistentSource;
        }

        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetDatabaseAuditListResponseDto))]
        [HttpPut]
        public IActionResult Put([FromBody] PagedDataOperators operators, int page = 1, int limit = 50)
        {
            var result = _databaseAuditPersistentSource.GetDatabaseAudit(limit, page, operators);
            return StatusCode(StatusCodes.Status200OK, result);
        }
    }
}
