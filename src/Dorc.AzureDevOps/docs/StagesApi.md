# Org.OpenAPITools.Api.StagesApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**StagesUpdate**](StagesApi.md#stagesupdate) | **PATCH** /{organization}/{project}/_apis/build/builds/{buildId}/stages/{stageRefName} |  |

<a name="stagesupdate"></a>
# **StagesUpdate**
> void StagesUpdate (string organization, int buildId, string stageRefName, string project, string apiVersion, UpdateStageParameters body)



Update a build stage

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class StagesUpdateExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new StagesApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var buildId = 56;  // int | 
            var stageRefName = "stageRefName_example";  // string | 
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var body = new UpdateStageParameters(); // UpdateStageParameters | 

            try
            {
                apiInstance.StagesUpdate(organization, buildId, stageRefName, project, apiVersion, body);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling StagesApi.StagesUpdate: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the StagesUpdateWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    apiInstance.StagesUpdateWithHttpInfo(organization, buildId, stageRefName, project, apiVersion, body);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling StagesApi.StagesUpdateWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **buildId** | **int** |  |  |
| **stageRefName** | **string** |  |  |
| **project** | **string** | Project ID or project name |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **body** | [**UpdateStageParameters**](UpdateStageParameters.md) |  |  |

### Return type

void (empty response body)

### Authorization

[oauth2](../README.md#oauth2)

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: Not defined


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

