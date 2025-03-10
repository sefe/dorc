/*
 * Build
 *
 * No description provided (generated by Openapi Generator https://github.com/openapitools/openapi-generator)
 *
 * The version of the OpenAPI document: 6.0
 * Contact: nugetvss@microsoft.com
 * Generated by: https://github.com/openapitools/openapi-generator.git
 */

using System;
using Xunit;
using Org.OpenAPITools.Api;
// uncomment below to import models
//using Org.OpenAPITools.Model;

namespace Org.OpenAPITools.Test.Api
{
    /// <summary>
    ///  Class for testing ResourcesApi
    /// </summary>
    /// <remarks>
    /// This file is automatically generated by OpenAPI Generator (https://openapi-generator.tech).
    /// Please update the test case below to test the API endpoint.
    /// </remarks>
    public class ResourcesApiTests : IDisposable
    {
        private ResourcesApi instance;

        public ResourcesApiTests()
        {
            instance = new ResourcesApi();
        }

        public void Dispose()
        {
            // Cleanup when everything is done.
        }

        /// <summary>
        /// Test an instance of ResourcesApi
        /// </summary>
        [Fact]
        public void InstanceTest()
        {
            // TODO uncomment below to test 'IsType' ResourcesApi
            //Assert.IsType<ResourcesApi>(instance);
        }

        /// <summary>
        /// Test ResourcesAuthorizeDefinitionResources
        /// </summary>
        [Fact]
        public void ResourcesAuthorizeDefinitionResourcesTest()
        {
            // TODO uncomment below to test the method and replace null with proper value
            //string organization = null;
            //string project = null;
            //int definitionId = null;
            //string apiVersion = null;
            //List<DefinitionResourceReference> body = null;
            //var response = instance.ResourcesAuthorizeDefinitionResources(organization, project, definitionId, apiVersion, body);
            //Assert.IsType<List<DefinitionResourceReference>>(response);
        }

        /// <summary>
        /// Test ResourcesList
        /// </summary>
        [Fact]
        public void ResourcesListTest()
        {
            // TODO uncomment below to test the method and replace null with proper value
            //string organization = null;
            //string project = null;
            //int definitionId = null;
            //string apiVersion = null;
            //var response = instance.ResourcesList(organization, project, definitionId, apiVersion);
            //Assert.IsType<List<DefinitionResourceReference>>(response);
        }
    }
}
