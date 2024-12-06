# Org.OpenAPITools.Api.ControllersApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**ControllersGet**](ControllersApi.md#controllersget) | **GET** /{organization}/_apis/build/controllers/{controllerId} |  |
| [**ControllersList**](ControllersApi.md#controllerslist) | **GET** /{organization}/_apis/build/controllers |  |

<a name="controllersget"></a>
# **ControllersGet**
> BuildController ControllersGet (string organization, int controllerId, string apiVersion)



Gets a controller

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class ControllersGetExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new ControllersApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var controllerId = 56;  // int | 
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.

            try
            {
                BuildController result = apiInstance.ControllersGet(organization, controllerId, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling ControllersApi.ControllersGet: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the ControllersGetWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<BuildController> response = apiInstance.ControllersGetWithHttpInfo(organization, controllerId, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling ControllersApi.ControllersGetWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **controllerId** | **int** |  |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |

### Return type

[**BuildController**](BuildController.md)

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

<a name="controllerslist"></a>
# **ControllersList**
> List&lt;BuildController&gt; ControllersList (string organization, string apiVersion, string? name = null)



Gets controller, optionally filtered by name

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class ControllersListExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new ControllersApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var name = "name_example";  // string? |  (optional) 

            try
            {
                List<BuildController> result = apiInstance.ControllersList(organization, apiVersion, name);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling ControllersApi.ControllersList: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the ControllersListWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<BuildController>> response = apiInstance.ControllersListWithHttpInfo(organization, apiVersion, name);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling ControllersApi.ControllersListWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |
| **name** | **string?** |  | [optional]  |

### Return type

[**List&lt;BuildController&gt;**](BuildController.md)

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

