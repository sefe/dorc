# Org.OpenAPITools.Api.BadgeApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**BadgeGet**](BadgeApi.md#badgeget) | **GET** /{organization}/_apis/public/build/definitions/{project}/{definitionId}/badge |  |
| [**BadgeGetBuildBadgeData**](BadgeApi.md#badgegetbuildbadgedata) | **GET** /{organization}/{project}/_apis/build/repos/{repoType}/badge |  |

<a name="badgeget"></a>
# **BadgeGet**
> string BadgeGet (string organization, Guid project, int definitionId, string apiVersion, string? branchName = null)



This endpoint is deprecated. Please see the Build Status REST endpoint.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BadgeGetExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new BadgeApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // Guid | The project ID or name.
            var definitionId = 56;  // int | The ID of the definition.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var branchName = "branchName_example";  // string? | The name of the branch. (optional) 

            try
            {
                string result = apiInstance.BadgeGet(organization, project, definitionId, apiVersion, branchName);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BadgeApi.BadgeGet: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BadgeGetWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<string> response = apiInstance.BadgeGetWithHttpInfo(organization, project, definitionId, apiVersion, branchName);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BadgeApi.BadgeGetWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **Guid** | The project ID or name. |  |
| **definitionId** | **int** | The ID of the definition. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |
| **branchName** | **string?** | The name of the branch. | [optional]  |

### Return type

**string**

### Authorization

[accessToken](../README.md#accessToken)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

<a name="badgegetbuildbadgedata"></a>
# **BadgeGetBuildBadgeData**
> string BadgeGetBuildBadgeData (string organization, string project, string repoType, string apiVersion, string? repoId = null, string? branchName = null)



Gets a badge that indicates the status of the most recent build for the specified branch.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BadgeGetBuildBadgeDataExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new BadgeApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var repoType = "repoType_example";  // string | The repository type.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.2' to use this version of the api.
            var repoId = "repoId_example";  // string? | The repository ID. (optional) 
            var branchName = "branchName_example";  // string? | The branch name. (optional) 

            try
            {
                string result = apiInstance.BadgeGetBuildBadgeData(organization, project, repoType, apiVersion, repoId, branchName);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BadgeApi.BadgeGetBuildBadgeData: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BadgeGetBuildBadgeDataWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<string> response = apiInstance.BadgeGetBuildBadgeDataWithHttpInfo(organization, project, repoType, apiVersion, repoId, branchName);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BadgeApi.BadgeGetBuildBadgeDataWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **repoType** | **string** | The repository type. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.2&#39; to use this version of the api. |  |
| **repoId** | **string?** | The repository ID. | [optional]  |
| **branchName** | **string?** | The branch name. | [optional]  |

### Return type

**string**

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

