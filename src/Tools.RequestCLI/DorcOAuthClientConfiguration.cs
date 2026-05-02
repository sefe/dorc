using Dorc.Core.Configuration;
using Microsoft.Extensions.Configuration;
using System;

namespace Tools.RequestCLI
{
    internal class DorcOAuthClientConfiguration : IOAuthClientConfiguration
    {
        private IConfiguration _config;

        public DorcOAuthClientConfiguration(IConfiguration config)
        {
            _config = config;

            ValidateConfiguration(config);
        }

        private void ValidateConfiguration(IConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(config["DorcApi:BaseUrl"]))
            {
                throw new InvalidOperationException("Dorc BaseUrl is not configured (DorcApi:BaseUrl).");
            }

            if (string.IsNullOrWhiteSpace(config["DorcApi:ClientId"]))
            {
                throw new InvalidOperationException("Dorc ClientId is not configured (DorcApi:ClientId).");
            }

            if (string.IsNullOrWhiteSpace(config["DorcApi:ClientSecret"]))
            {
                throw new InvalidOperationException("Dorc ClientSecret is not configured (DorcApi:ClientSecret).");
            }
        }

        public string BaseUrl 
        { 
            get
            {
                return _config["DorcApi:BaseUrl"];
            }
        }

        public string ClientId 
        {
            get
            {
                return _config["DorcApi:ClientId"];
            }
        }

        public string ClientSecret 
        {
            get
            {
                return _config["DorcApi:ClientSecret"];
            }
        }

        public string Scope 
        {
            get
            {
                return _config["DorcApi:Scope"] ?? "dorc-api.manage";
            }
        }
    }
}
