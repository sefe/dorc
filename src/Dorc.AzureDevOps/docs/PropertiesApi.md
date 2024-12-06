# Org.OpenAPITools.Api.PropertiesApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**PropertiesGetBuildProperties**](PropertiesApi.md#propertiesgetbuildproperties) | **GET** /{organization}/{project}/_apis/build/builds/{buildId}/properties |  |
| [**PropertiesGetDefinitionProperties**](PropertiesApi.md#propertiesgetdefinitionproperties) | **GET** /{organization}/{project}/_apis/build/definitions/{definitionId}/properties |  |
| [**PropertiesUpdateBuildProperties**](PropertiesApi.md#propertiesupdatebuildproperties) | **PATCH** /{organization}/{project}/_apis/build/builds/{buildId}/properties |  |
| [**PropertiesUpdateDefinitionProperties**](PropertiesApi.md#propertiesupdatedefinitionproperties) | **PATCH** /{organization}/{project}/_apis/build/definitions/{definitionId}/properties |  |

<a name="propertiesgetbuildproperties"></a>
# **PropertiesGetBuildProperties**
> PropertiesCollection PropertiesGetBuildProperties (string organization, string project, int buildId, string apiVersion, string? filter = null)



Gets properties for a build.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class PropertiesGetBuildPropertiesExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new PropertiesApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var filter = "filter_example";  // string? | A comma-delimited list of properties. If specified, filters to these specific properties. (optional) 

            try
            {
                PropertiesCollection result = apiInstance.PropertiesGetBuildProperties(organization, project, buildId, apiVersion, filter);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling PropertiesApi.PropertiesGetBuildProperties: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the PropertiesGetBuildPropertiesWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<PropertiesCollection> response = apiInstance.PropertiesGetBuildPropertiesWithHttpInfo(organization, project, buildId, apiVersion, filter);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling PropertiesApi.PropertiesGetBuildPropertiesWithHttpInfo: " + e.Message);
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
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **filter** | **string?** | A comma-delimited list of properties. If specified, filters to these specific properties. | [optional]  |

### Return type

[**PropertiesCollection**](PropertiesCollection.md)

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

<a name="propertiesgetdefinitionproperties"></a>
# **PropertiesGetDefinitionProperties**
> PropertiesCollection PropertiesGetDefinitionProperties (string organization, string project, int definitionId, string apiVersion, string? filter = null)



Gets properties for a definition.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class PropertiesGetDefinitionPropertiesExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new PropertiesApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definitionId = 56;  // int | The ID of the definition.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var filter = "filter_example";  // string? | A comma-delimited list of properties. If specified, filters to these specific properties. (optional) 

            try
            {
                PropertiesCollection result = apiInstance.PropertiesGetDefinitionProperties(organization, project, definitionId, apiVersion, filter);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling PropertiesApi.PropertiesGetDefinitionProperties: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the PropertiesGetDefinitionPropertiesWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<PropertiesCollection> response = apiInstance.PropertiesGetDefinitionPropertiesWithHttpInfo(organization, project, definitionId, apiVersion, filter);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling PropertiesApi.PropertiesGetDefinitionPropertiesWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **definitionId** | **int** | The ID of the definition. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **filter** | **string?** | A comma-delimited list of properties. If specified, filters to these specific properties. | [optional]  |

### Return type

[**PropertiesCollection**](PropertiesCollection.md)

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

<a name="propertiesupdatebuildproperties"></a>
# **PropertiesUpdateBuildProperties**
> PropertiesCollection PropertiesUpdateBuildProperties (string organization, string project, int buildId, string apiVersion, JsonPatchDocument body)



Updates properties for a build.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class PropertiesUpdateBuildPropertiesExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new PropertiesApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var body = new JsonPatchDocument(); // JsonPatchDocument | A json-patch document describing the properties to update.

            try
            {
                PropertiesCollection result = apiInstance.PropertiesUpdateBuildProperties(organization, project, buildId, apiVersion, body);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling PropertiesApi.PropertiesUpdateBuildProperties: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the PropertiesUpdateBuildPropertiesWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<PropertiesCollection> response = apiInstance.PropertiesUpdateBuildPropertiesWithHttpInfo(organization, project, buildId, apiVersion, body);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling PropertiesApi.PropertiesUpdateBuildPropertiesWithHttpInfo: " + e.Message);
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
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **body** | [**JsonPatchDocument**](JsonPatchDocument.md) | A json-patch document describing the properties to update. |  |

### Return type

[**PropertiesCollection**](PropertiesCollection.md)

### Authorization

[oauth2](../README.md#oauth2)

### HTTP request headers

 - **Content-Type**: application/json-patch+json
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

<a name="propertiesupdatedefinitionproperties"></a>
# **PropertiesUpdateDefinitionProperties**
> PropertiesCollection PropertiesUpdateDefinitionProperties (string organization, string project, int definitionId, string apiVersion, JsonPatchDocument body)



Updates properties for a definition.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class PropertiesUpdateDefinitionPropertiesExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new PropertiesApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definitionId = 56;  // int | The ID of the definition.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var body = new JsonPatchDocument(); // JsonPatchDocument | A json-patch document describing the properties to update.

            try
            {
                PropertiesCollection result = apiInstance.PropertiesUpdateDefinitionProperties(organization, project, definitionId, apiVersion, body);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling PropertiesApi.PropertiesUpdateDefinitionProperties: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the PropertiesUpdateDefinitionPropertiesWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<PropertiesCollection> response = apiInstance.PropertiesUpdateDefinitionPropertiesWithHttpInfo(organization, project, definitionId, apiVersion, body);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling PropertiesApi.PropertiesUpdateDefinitionPropertiesWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **definitionId** | **int** | The ID of the definition. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **body** | [**JsonPatchDocument**](JsonPatchDocument.md) | A json-patch document describing the properties to update. |  |

### Return type

[**PropertiesCollection**](PropertiesCollection.md)

### Authorization

[oauth2](../README.md#oauth2)

### HTTP request headers

 - **Content-Type**: application/json-patch+json
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

