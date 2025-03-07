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
    ///  Class for testing TimelineApi
    /// </summary>
    /// <remarks>
    /// This file is automatically generated by OpenAPI Generator (https://openapi-generator.tech).
    /// Please update the test case below to test the API endpoint.
    /// </remarks>
    public class TimelineApiTests : IDisposable
    {
        private TimelineApi instance;

        public TimelineApiTests()
        {
            instance = new TimelineApi();
        }

        public void Dispose()
        {
            // Cleanup when everything is done.
        }

        /// <summary>
        /// Test an instance of TimelineApi
        /// </summary>
        [Fact]
        public void InstanceTest()
        {
            // TODO uncomment below to test 'IsType' TimelineApi
            //Assert.IsType<TimelineApi>(instance);
        }

        /// <summary>
        /// Test TimelineGet
        /// </summary>
        [Fact]
        public void TimelineGetTest()
        {
            // TODO uncomment below to test the method and replace null with proper value
            //string organization = null;
            //string project = null;
            //int buildId = null;
            //Guid timelineId = null;
            //string apiVersion = null;
            //int? changeId = null;
            //Guid? planId = null;
            //var response = instance.TimelineGet(organization, project, buildId, timelineId, apiVersion, changeId, planId);
            //Assert.IsType<Timeline>(response);
        }
    }
}
