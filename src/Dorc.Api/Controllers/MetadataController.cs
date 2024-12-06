using log4net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class MetadataController : ControllerBase
    {
        private readonly ILog _logger;

        public MetadataController(ILog logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Get the environment and version of the application
        /// </summary>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(string))]
        [HttpGet]
        public IResult Get()
        {
            var env = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetSection("AppSettings")[
                "environment"] ?? "nd";
            var envAndVersion = $"{env} - {GetType().Assembly.GetName().Version}";

            _logger.Debug("Starting with " + envAndVersion);

            return Results.Ok(envAndVersion);
        }
    }
}