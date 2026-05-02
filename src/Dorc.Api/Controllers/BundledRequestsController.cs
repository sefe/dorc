using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.Versioning;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [SupportedOSPlatform("windows")]
    [ApiController]
    [Route("[controller]")]
    public class BundledRequestsController : ControllerBase
    {
        private readonly ILogger _logger;
        private readonly IBundledRequestsPersistentSource _bundledRequestsPersistentSource;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;

        public BundledRequestsController(
            ILogger<BundledRequestsController> logger,
            IBundledRequestsPersistentSource bundledRequestsPersistentSource,
            ISecurityPrivilegesChecker securityPrivilegesChecker)
        {
            _bundledRequestsPersistentSource = bundledRequestsPersistentSource;
            _logger = logger;
            _securityPrivilegesChecker = securityPrivilegesChecker;
        }

        /// <summary>
        /// Get the list of request bundles
        /// </summary>
        /// <param name="projectNames"></param>
        /// <returns></returns>
        [HttpGet]
        [Produces(typeof(IEnumerable<BundledRequestsApiModel>))]
        public IActionResult Get([FromQuery] List<string> projectNames)
        {
            try
            {
                List<BundledRequestsApiModel> output = new();
                foreach (var p in projectNames)
                {
                    output.AddRange(_bundledRequestsPersistentSource.GetBundles(p));
                }

                return Ok(output);
            }
            catch (Exception ex)
            {
                string error = "Error while locating Bundled Requests for project(s) " +
                               string.Join('|', projectNames);
                _logger.LogError(ex, error);
                return BadRequest(error);
            }
        }

        /// <summary>
        /// Get requests for a specific bundle
        /// </summary>
        /// <param name="bundleName"></param>
        /// <returns></returns>
        [HttpGet]
        [Produces(typeof(IEnumerable<BundledRequestsApiModel>))]
        [Route("RequestsForBundle")]
        public IActionResult GetRequestsForBundle([FromQuery] string bundleName)
        {
            try
            {
                var requests = _bundledRequestsPersistentSource.GetRequestsForBundle(bundleName);
                return Ok(requests);
            }
            catch (Exception ex)
            {
                string error = $"Error while locating requests for bundle {bundleName}";
                _logger.LogError(ex, error);
                return BadRequest(error + " - " + ex);
            }
        }

        /// <summary>
        /// Create a new bundled request
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        public IActionResult Create([FromBody] BundledRequestsApiModel model)
        {
            try
            {
                if (model == null)
                {
                    return BadRequest("Invalid request data.");
                }

                if (!model.ProjectId.HasValue)
                {
                    return BadRequest("ProjectId must be provided for bundled requests.");
                }

                // Check user has write/modify rights for the project
                if (!_securityPrivilegesChecker.CanModifyProject(User, (int)model.ProjectId.Value))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, "User does not have Modify rights on this Project");
                }

                _bundledRequestsPersistentSource.AddRequestToBundle(model);
                return Ok("Bundled request created successfully.");
            }
            catch (Exception ex)
            {
                string error = "Error while creating bundled request.";
                _logger.LogError(ex, error);
                return BadRequest(error + " - " + ex);
            }
        }

        /// <summary>
        /// Update an existing bundled request
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPut]
        public IActionResult Update([FromBody] BundledRequestsApiModel model)
        {
            try
            {
                if (model == null)
                {
                    return BadRequest("Invalid request data.");
                }

                if (!model.ProjectId.HasValue)
                {
                    return BadRequest("ProjectId must be provided for bundled requests.");
                }

                // Check user has write/modify rights for the project
                if (!_securityPrivilegesChecker.CanModifyProject(User, (int)model.ProjectId.Value))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, "User does not have Modify rights on this Project");
                }

                _bundledRequestsPersistentSource.UpdateRequestInBundle(model);
                return Ok("Bundled request updated successfully.");
            }
            catch (Exception ex)
            {
                string error = "Error while updating bundled request.";
                _logger.LogError(ex, error);
                return BadRequest(error + " - " + ex);
            }
        }

        /// <summary>
        /// Delete a bundled request
        /// </summary>
        /// <param name="id">The ID of the bundled request to delete</param>
        /// <returns></returns>
        [HttpDelete]
        public IActionResult Delete([FromQuery] int id)
        {
            try
            {
                // Note: delete permission check is not changed here. If you need
                // delete to be protected by project write rights, add a lookup
                // for the bundle/project and check CanModifyProject like above.

                // Call the persistent source to delete the bundled request by ID
                _bundledRequestsPersistentSource.DeleteRequestFromBundle(id);

                return Ok($"Bundled request with ID {id} deleted successfully.");
            }
            catch (Exception ex)
            {
                string error = $"Error while deleting bundled request with ID {id}";
                _logger.LogError(ex, error);
                return BadRequest(error + " - " + ex);
            }
        }

    }
}