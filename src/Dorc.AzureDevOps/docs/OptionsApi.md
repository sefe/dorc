# Org.OpenAPITools.Api.OptionsApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**OptionsList**](OptionsApi.md#optionslist) | **GET** /{organization}/{project}/_apis/build/options |  |

<a name="optionslist"></a>
# **OptionsList**
> List&lt;BuildOptionDefinition&gt; OptionsList (string organization, string project, string apiVersion)



Gets all build definition options supported by the system.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class OptionsListExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new OptionsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.

            try
            {
                List<BuildOptionDefinition> result = apiInstance.OptionsList(organization, project, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling OptionsApi.OptionsList: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the OptionsListWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<BuildOptionDefinition>> response = apiInstance.OptionsListWithHttpInfo(organization, project, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling OptionsApi.OptionsListWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |

### Return type

[**List&lt;BuildOptionDefinition&gt;**](BuildOptionDefinition.md)

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

