# Org.OpenAPITools.Api.StatusApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**StatusGet**](StatusApi.md#statusget) | **GET** /{organization}/{project}/_apis/build/status/{definition} |  |

<a name="statusget"></a>
# **StatusGet**
> string StatusGet (string organization, string project, string definition, string apiVersion, string? branchName = null, string? stageName = null, string? jobName = null, string? configuration = null, string? label = null)



<p>Gets the build status for a definition, optionally scoped to a specific branch, stage, job, and configuration.</p> <p>If there are more than one, then it is required to pass in a stageName value when specifying a jobName, and the same rule then applies for both if passing a configuration parameter.</p>

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class StatusGetExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            var apiInstance = new StatusApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definition = "definition_example";  // string | Either the definition name with optional leading folder path, or the definition id.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var branchName = "branchName_example";  // string? | Only consider the most recent build for this branch. (optional) 
            var stageName = "stageName_example";  // string? | Use this stage within the pipeline to render the status. (optional) 
            var jobName = "jobName_example";  // string? | Use this job within a stage of the pipeline to render the status. (optional) 
            var configuration = "configuration_example";  // string? | Use this job configuration to render the status (optional) 
            var label = "label_example";  // string? | Replaces the default text on the left side of the badge. (optional) 

            try
            {
                string result = apiInstance.StatusGet(organization, project, definition, apiVersion, branchName, stageName, jobName, configuration, label);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling StatusApi.StatusGet: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the StatusGetWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<string> response = apiInstance.StatusGetWithHttpInfo(organization, project, definition, apiVersion, branchName, stageName, jobName, configuration, label);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling StatusApi.StatusGetWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **definition** | **string** | Either the definition name with optional leading folder path, or the definition id. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **branchName** | **string?** | Only consider the most recent build for this branch. | [optional]  |
| **stageName** | **string?** | Use this stage within the pipeline to render the status. | [optional]  |
| **jobName** | **string?** | Use this job within a stage of the pipeline to render the status. | [optional]  |
| **configuration** | **string?** | Use this job configuration to render the status | [optional]  |
| **label** | **string?** | Replaces the default text on the left side of the badge. | [optional]  |

### Return type

**string**

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

