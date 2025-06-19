using System.Runtime.Versioning;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [SupportedOSPlatform("windows")]
    [ApiController]
    [Route("[controller]")]
    public class BundledRequestsController : ControllerBase
    {
        private readonly ILog _logger;
        private readonly IBundledRequestsPersistentSource _bundledRequestsPersistentSource;

        public BundledRequestsController(ILog logger,
            IBundledRequestsPersistentSource bundledRequestsPersistentSource)
        {
            _bundledRequestsPersistentSource = bundledRequestsPersistentSource;
            _logger = logger;
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
                _logger.Error(error, ex);
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
                _logger.Error(error, ex);
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

                // Assuming the persistent source has a method to add a new bundle
                _bundledRequestsPersistentSource.AddRequestToBundle(model);
                return Ok("Bundled request created successfully.");
            }
            catch (Exception ex)
            {
                string error = "Error while creating bundled request.";
                _logger.Error(error, ex);
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

                // Assuming the persistent source has a method to update a bundle
                _bundledRequestsPersistentSource.UpdateRequestInBundle(model);
                return Ok("Bundled request updated successfully.");
            }
            catch (Exception ex)
            {
                string error = "Error while updating bundled request.";
                _logger.Error(error, ex);
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
                // Call the persistent source to delete the bundled request by ID
                _bundledRequestsPersistentSource.DeleteRequestFromBundle(id);

                return Ok($"Bundled request with ID {id} deleted successfully.");
            }
            catch (Exception ex)
            {
                string error = $"Error while deleting bundled request with ID {id}";
                _logger.Error(error, ex);
                return BadRequest(error + " - " + ex);
            }
        }

    }
}