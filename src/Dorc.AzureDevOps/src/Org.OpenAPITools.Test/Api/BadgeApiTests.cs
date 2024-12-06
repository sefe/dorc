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

namespace Org.OpenAPITools.Test.Api
{
    /// <summary>
    ///  Class for testing BadgeApi
    /// </summary>
    /// <remarks>
    /// This file is automatically generated by OpenAPI Generator (https://openapi-generator.tech).
    /// Please update the test case below to test the API endpoint.
    /// </remarks>
    public class BadgeApiTests : IDisposable
    {
        private BadgeApi instance;

        public BadgeApiTests()
        {
            instance = new BadgeApi();
        }

        public void Dispose()
        {
            // Cleanup when everything is done.
        }

        /// <summary>
        /// Test an instance of BadgeApi
        /// </summary>
        [Fact]
        public void InstanceTest()
        {
            // TODO uncomment below to test 'IsType' BadgeApi
            //Assert.IsType<BadgeApi>(instance);
        }

        /// <summary>
        /// Test BadgeGet
        /// </summary>
        [Fact]
        public void BadgeGetTest()
        {
            // TODO uncomment below to test the method and replace null with proper value
            //string organization = null;
            //Guid project = null;
            //int definitionId = null;
            //string apiVersion = null;
            //string? branchName = null;
            //var response = instance.BadgeGet(organization, project, definitionId, apiVersion, branchName);
            //Assert.IsType<string>(response);
        }

        /// <summary>
        /// Test BadgeGetBuildBadgeData
        /// </summary>
        [Fact]
        public void BadgeGetBuildBadgeDataTest()
        {
            // TODO uncomment below to test the method and replace null with proper value
            //string organization = null;
            //string project = null;
            //string repoType = null;
            //string apiVersion = null;
            //string? repoId = null;
            //string? branchName = null;
            //var response = instance.BadgeGetBuildBadgeData(organization, project, repoType, apiVersion, repoId, branchName);
            //Assert.IsType<string>(response);
        }
    }
}
