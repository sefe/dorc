# Org.OpenAPITools.Api.ReportApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**ReportGet**](ReportApi.md#reportget) | **GET** /{organization}/{project}/_apis/build/builds/{buildId}/report |  |

<a name="reportget"></a>
# **ReportGet**
> BuildReportMetadata ReportGet (string organization, string project, int buildId, string apiVersion, string? type = null)



Gets a build report.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class ReportGetExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new ReportApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.2' to use this version of the api.
            var type = "type_example";  // string? |  (optional) 

            try
            {
                BuildReportMetadata result = apiInstance.ReportGet(organization, project, buildId, apiVersion, type);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling ReportApi.ReportGet: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the ReportGetWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<BuildReportMetadata> response = apiInstance.ReportGetWithHttpInfo(organization, project, buildId, apiVersion, type);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling ReportApi.ReportGetWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **buildId** | **int** | The ID of the build. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.2&#39; to use this version of the api. |  |
| **type** | **string?** |  | [optional]  |

### Return type

[**BuildReportMetadata**](BuildReportMetadata.md)

### Authorization

[oauth2](../README.md#oauth2)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: text/html, application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

