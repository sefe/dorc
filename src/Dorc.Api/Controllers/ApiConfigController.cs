using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [AllowAnonymous]
    [ApiController]
    [Route("[controller]")]
    public class ApiConfigController : ControllerBase
    {
        private readonly IConfigurationSettings _configurationSettings;

        public ApiConfigController(IConfigurationSettings configurationSettings)
        {
            _configurationSettings = configurationSettings;
        }

        /// <summary>
        /// Exposes limited Api configuration parameters
        /// </summary>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiConfigModel))]
        [HttpGet]
        public ApiConfigModel Get()
        {
            var scheme = _configurationSettings.GetAuthenticationScheme();
            return new ApiConfigModel
            {
                AuthenticationScheme = scheme == ConfigAuthScheme.Both ? ConfigAuthScheme.OAuth : scheme,
                OAuthAuthority = _configurationSettings.GetOAuthAuthority()
            };
        }
    }
}
