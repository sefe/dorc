# Org.OpenAPITools.Api.ResourcesApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**ResourcesAuthorizeDefinitionResources**](ResourcesApi.md#resourcesauthorizedefinitionresources) | **PATCH** /{organization}/{project}/_apis/build/definitions/{definitionId}/resources |  |
| [**ResourcesList**](ResourcesApi.md#resourceslist) | **GET** /{organization}/{project}/_apis/build/definitions/{definitionId}/resources |  |

<a name="resourcesauthorizedefinitionresources"></a>
# **ResourcesAuthorizeDefinitionResources**
> List&lt;DefinitionResourceReference&gt; ResourcesAuthorizeDefinitionResources (string organization, string project, int definitionId, string apiVersion, List<DefinitionResourceReference> body)



### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class ResourcesAuthorizeDefinitionResourcesExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new ResourcesApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definitionId = 56;  // int | 
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var body = new List<DefinitionResourceReference>(); // List<DefinitionResourceReference> | 

            try
            {
                List<DefinitionResourceReference> result = apiInstance.ResourcesAuthorizeDefinitionResources(organization, project, definitionId, apiVersion, body);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling ResourcesApi.ResourcesAuthorizeDefinitionResources: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the ResourcesAuthorizeDefinitionResourcesWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<DefinitionResourceReference>> response = apiInstance.ResourcesAuthorizeDefinitionResourcesWithHttpInfo(organization, project, definitionId, apiVersion, body);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling ResourcesApi.ResourcesAuthorizeDefinitionResourcesWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **definitionId** | **int** |  |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **body** | [**List&lt;DefinitionResourceReference&gt;**](DefinitionResourceReference.md) |  |  |

### Return type

[**List&lt;DefinitionResourceReference&gt;**](DefinitionResourceReference.md)

### Authorization

[oauth2](../README.md#oauth2)

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

<a name="resourceslist"></a>
# **ResourcesList**
> List&lt;DefinitionResourceReference&gt; ResourcesList (string organization, string project, int definitionId, string apiVersion)



### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class ResourcesListExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new ResourcesApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definitionId = 56;  // int | 
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.

            try
            {
                List<DefinitionResourceReference> result = apiInstance.ResourcesList(organization, project, definitionId, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling ResourcesApi.ResourcesList: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the ResourcesListWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<DefinitionResourceReference>> response = apiInstance.ResourcesListWithHttpInfo(organization, project, definitionId, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling ResourcesApi.ResourcesListWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **definitionId** | **int** |  |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |

### Return type

[**List&lt;DefinitionResourceReference&gt;**](DefinitionResourceReference.md)

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

