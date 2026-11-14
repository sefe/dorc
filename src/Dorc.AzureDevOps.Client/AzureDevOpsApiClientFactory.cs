using System;
using System.Net.Http;
﻿using Org.OpenAPITools.Client;

namespace Dorc.AzureDevOps.Client
{
    /// <summary>
    /// Builds the <see cref="ApiClient"/> the generated Azure DevOps API
    /// classes are constructed with, so every request deserializes through
    /// <see cref="AzureDevOpsListResponseConverter"/> with HTTP auto-redirects
    /// disabled. Pass the same instance as both the synchronous and asynchronous
    /// client: <c>new BuildsApi(apiClient, apiClient, configuration)</c>.
    /// </summary>
    public static class AzureDevOpsApiClientFactory
    {
        public static ApiClient Create(string basePath, Func<bool, HttpMessageHandler> httpHandlerFactory = null)
        {
            var apiClient = new AzureDevOpsApiClient(basePath, httpHandlerFactory);
            apiClient.SerializerSettings.Converters.Add(new AzureDevOpsListResponseConverter());
            return apiClient;
        }
    }
}
