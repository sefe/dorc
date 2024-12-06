# Org.OpenAPITools.Api.AuthorizedresourcesApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**AuthorizedresourcesAuthorizeProjectResources**](AuthorizedresourcesApi.md#authorizedresourcesauthorizeprojectresources) | **PATCH** /{organization}/{project}/_apis/build/authorizedresources |  |
| [**AuthorizedresourcesList**](AuthorizedresourcesApi.md#authorizedresourceslist) | **GET** /{organization}/{project}/_apis/build/authorizedresources |  |

<a name="authorizedresourcesauthorizeprojectresources"></a>
# **AuthorizedresourcesAuthorizeProjectResources**
> List&lt;DefinitionResourceReference&gt; AuthorizedresourcesAuthorizeProjectResources (string organization, string project, string apiVersion, List<DefinitionResourceReference> body)



### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class AuthorizedresourcesAuthorizeProjectResourcesExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new AuthorizedresourcesApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var body = new List<DefinitionResourceReference>(); // List<DefinitionResourceReference> | 

            try
            {
                List<DefinitionResourceReference> result = apiInstance.AuthorizedresourcesAuthorizeProjectResources(organization, project, apiVersion, body);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling AuthorizedresourcesApi.AuthorizedresourcesAuthorizeProjectResources: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the AuthorizedresourcesAuthorizeProjectResourcesWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<DefinitionResourceReference>> response = apiInstance.AuthorizedresourcesAuthorizeProjectResourcesWithHttpInfo(organization, project, apiVersion, body);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling AuthorizedresourcesApi.AuthorizedresourcesAuthorizeProjectResourcesWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
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

<a name="authorizedresourceslist"></a>
# **AuthorizedresourcesList**
> List&lt;DefinitionResourceReference&gt; AuthorizedresourcesList (string organization, string project, string apiVersion, string? type = null, string? id = null)



### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class AuthorizedresourcesListExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new AuthorizedresourcesApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var type = "type_example";  // string? |  (optional) 
            var id = "id_example";  // string? |  (optional) 

            try
            {
                List<DefinitionResourceReference> result = apiInstance.AuthorizedresourcesList(organization, project, apiVersion, type, id);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling AuthorizedresourcesApi.AuthorizedresourcesList: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the AuthorizedresourcesListWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<DefinitionResourceReference>> response = apiInstance.AuthorizedresourcesListWithHttpInfo(organization, project, apiVersion, type, id);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling AuthorizedresourcesApi.AuthorizedresourcesListWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **type** | **string?** |  | [optional]  |
| **id** | **string?** |  | [optional]  |

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

