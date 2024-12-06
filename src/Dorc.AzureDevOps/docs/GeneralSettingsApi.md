# Org.OpenAPITools.Api.GeneralSettingsApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**GeneralSettingsGet**](GeneralSettingsApi.md#generalsettingsget) | **GET** /{organization}/{project}/_apis/build/generalsettings |  |
| [**GeneralSettingsUpdate**](GeneralSettingsApi.md#generalsettingsupdate) | **PATCH** /{organization}/{project}/_apis/build/generalsettings |  |

<a name="generalsettingsget"></a>
# **GeneralSettingsGet**
> PipelineGeneralSettings GeneralSettingsGet (string organization, string project, string apiVersion)



Gets pipeline general settings.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class GeneralSettingsGetExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new GeneralSettingsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.

            try
            {
                PipelineGeneralSettings result = apiInstance.GeneralSettingsGet(organization, project, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling GeneralSettingsApi.GeneralSettingsGet: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the GeneralSettingsGetWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<PipelineGeneralSettings> response = apiInstance.GeneralSettingsGetWithHttpInfo(organization, project, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling GeneralSettingsApi.GeneralSettingsGetWithHttpInfo: " + e.Message);
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

### Return type

[**PipelineGeneralSettings**](PipelineGeneralSettings.md)

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

<a name="generalsettingsupdate"></a>
# **GeneralSettingsUpdate**
> PipelineGeneralSettings GeneralSettingsUpdate (string organization, string project, string apiVersion, PipelineGeneralSettings body)



Updates pipeline general settings.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class GeneralSettingsUpdateExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new GeneralSettingsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var body = new PipelineGeneralSettings(); // PipelineGeneralSettings | 

            try
            {
                PipelineGeneralSettings result = apiInstance.GeneralSettingsUpdate(organization, project, apiVersion, body);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling GeneralSettingsApi.GeneralSettingsUpdate: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the GeneralSettingsUpdateWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<PipelineGeneralSettings> response = apiInstance.GeneralSettingsUpdateWithHttpInfo(organization, project, apiVersion, body);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling GeneralSettingsApi.GeneralSettingsUpdateWithHttpInfo: " + e.Message);
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
| **body** | [**PipelineGeneralSettings**](PipelineGeneralSettings.md) |  |  |

### Return type

[**PipelineGeneralSettings**](PipelineGeneralSettings.md)

### Authorization

[accessToken](../README.md#accessToken)

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

