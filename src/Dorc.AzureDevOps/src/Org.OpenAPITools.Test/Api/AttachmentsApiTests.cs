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
    ///  Class for testing AttachmentsApi
    /// </summary>
    /// <remarks>
    /// This file is automatically generated by OpenAPI Generator (https://openapi-generator.tech).
    /// Please update the test case below to test the API endpoint.
    /// </remarks>
    public class AttachmentsApiTests : IDisposable
    {
        private AttachmentsApi instance;

        public AttachmentsApiTests()
        {
            instance = new AttachmentsApi();
        }

        public void Dispose()
        {
            // Cleanup when everything is done.
        }

        /// <summary>
        /// Test an instance of AttachmentsApi
        /// </summary>
        [Fact]
        public void InstanceTest()
        {
            // TODO uncomment below to test 'IsType' AttachmentsApi
            //Assert.IsType<AttachmentsApi>(instance);
        }

        /// <summary>
        /// Test AttachmentsGet
        /// </summary>
        [Fact]
        public void AttachmentsGetTest()
        {
            // TODO uncomment below to test the method and replace null with proper value
            //string organization = null;
            //string project = null;
            //int buildId = null;
            //Guid timelineId = null;
            //Guid recordId = null;
            //string type = null;
            //string name = null;
            //string apiVersion = null;
            //var response = instance.AttachmentsGet(organization, project, buildId, timelineId, recordId, type, name, apiVersion);
            //Assert.IsType<string>(response);
        }

        /// <summary>
        /// Test AttachmentsList
        /// </summary>
        [Fact]
        public void AttachmentsListTest()
        {
            // TODO uncomment below to test the method and replace null with proper value
            //string organization = null;
            //string project = null;
            //int buildId = null;
            //string type = null;
            //string apiVersion = null;
            //var response = instance.AttachmentsList(organization, project, buildId, type, apiVersion);
            //Assert.IsType<List<Attachment>>(response);
        }
    }
}
