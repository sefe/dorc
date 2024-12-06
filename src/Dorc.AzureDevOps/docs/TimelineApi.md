# Org.OpenAPITools.Api.TimelineApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**TimelineGet**](TimelineApi.md#timelineget) | **GET** /{organization}/{project}/_apis/build/builds/{buildId}/timeline/{timelineId} |  |

<a name="timelineget"></a>
# **TimelineGet**
> Timeline TimelineGet (string organization, string project, int buildId, Guid timelineId, string apiVersion, int? changeId = null, Guid? planId = null)



Gets details for a build

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class TimelineGetExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new TimelineApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | 
            var timelineId = "timelineId_example";  // Guid | 
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var changeId = 56;  // int? |  (optional) 
            var planId = "planId_example";  // Guid? |  (optional) 

            try
            {
                Timeline result = apiInstance.TimelineGet(organization, project, buildId, timelineId, apiVersion, changeId, planId);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling TimelineApi.TimelineGet: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the TimelineGetWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<Timeline> response = apiInstance.TimelineGetWithHttpInfo(organization, project, buildId, timelineId, apiVersion, changeId, planId);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling TimelineApi.TimelineGetWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **buildId** | **int** |  |  |
| **timelineId** | **Guid** |  |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |
| **changeId** | **int?** |  | [optional]  |
| **planId** | **Guid?** |  | [optional]  |

### Return type

[**Timeline**](Timeline.md)

### Authorization

[oauth2](../README.md#oauth2)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

