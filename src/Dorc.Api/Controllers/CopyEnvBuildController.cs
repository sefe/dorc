using Dorc.ApiModel;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class CopyEnvBuildController : ControllerBase
    {
        private readonly ISecurityPrivilegesChecker _apiSecurityService;
        private readonly ILogger<CopyEnvBuildController> _log;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly IDeployLibrary _deployLibrary;

        public CopyEnvBuildController(
            ISecurityPrivilegesChecker apiSecurityService,
            ILogger<CopyEnvBuildController> log,
            IClaimsPrincipalReader claimsPrincipalReader,
            IDeployLibrary deployLibrary)
        {
            _apiSecurityService = apiSecurityService;
            _log = log;
            _claimsPrincipalReader = claimsPrincipalReader;
            _deployLibrary = deployLibrary;
        }

        /// <summary>
        /// Copy Environment Build
        /// </summary>
        /// <param name="copyEnvBuildDto"></param>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(CopyEnvBuildResponseDto))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [HttpPost]
        public async Task<IActionResult> Post([FromBody] CopyEnvBuildDto copyEnvBuildDto)
        {
            try
            {
                var canModifyEnv = _apiSecurityService.CanModifyEnvironment(User, copyEnvBuildDto.TargetEnv);
                if (!canModifyEnv)
                {
                    string username = _claimsPrincipalReader.GetUserFullDomainName(User);
                    _log.LogInformation($"Forbidden CopyEnvBuild request to {copyEnvBuildDto.TargetEnv} from {username}");
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"Forbidden request to {copyEnvBuildDto.TargetEnv} from {username}");
                }

                List<int> requestIds;

                if (!string.IsNullOrEmpty(copyEnvBuildDto.Components))
                {
                    requestIds = _deployLibrary.DeployCopyEnvBuildWithComponentNames(
                        copyEnvBuildDto.SourceEnv,
                        copyEnvBuildDto.TargetEnv,
                        copyEnvBuildDto.Project,
                        copyEnvBuildDto.Components,
                        User);
                }
                else
                {
                    requestIds = _deployLibrary.CopyEnvBuildAllComponents(
                        copyEnvBuildDto.SourceEnv,
                        copyEnvBuildDto.TargetEnv,
                        copyEnvBuildDto.Project,
                        User);
                }

                _log.LogInformation($"CopyEnvBuild created {requestIds.Count} request(s) from {copyEnvBuildDto.SourceEnv} to {copyEnvBuildDto.TargetEnv}");

                return Ok(new CopyEnvBuildResponseDto
                {
                    RequestIds = requestIds,
                    Success = true,
                    Message = $"Successfully created {requestIds.Count} request(s)"
                });
            }
            catch (Exception e)
            {
                _log.LogError(e, "CopyEnvBuild");
                return BadRequest(new CopyEnvBuildResponseDto
                {
                    RequestIds = new List<int>(),
                    Success = false,
                    Message = e.Message
                });
            }
        }
    }
}