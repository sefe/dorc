# Org.OpenAPITools.Api.AttachmentsApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**AttachmentsGet**](AttachmentsApi.md#attachmentsget) | **GET** /{organization}/{project}/_apis/build/builds/{buildId}/{timelineId}/{recordId}/attachments/{type}/{name} |  |
| [**AttachmentsList**](AttachmentsApi.md#attachmentslist) | **GET** /{organization}/{project}/_apis/build/builds/{buildId}/attachments/{type} |  |

<a name="attachmentsget"></a>
# **AttachmentsGet**
> string AttachmentsGet (string organization, string project, int buildId, Guid timelineId, Guid recordId, string type, string name, string apiVersion)



Gets a specific attachment.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class AttachmentsGetExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new AttachmentsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var timelineId = "timelineId_example";  // Guid | The ID of the timeline.
            var recordId = "recordId_example";  // Guid | The ID of the timeline record.
            var type = "type_example";  // string | The type of the attachment.
            var name = "name_example";  // string | The name of the attachment.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.2' to use this version of the api.

            try
            {
                string result = apiInstance.AttachmentsGet(organization, project, buildId, timelineId, recordId, type, name, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling AttachmentsApi.AttachmentsGet: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the AttachmentsGetWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<string> response = apiInstance.AttachmentsGetWithHttpInfo(organization, project, buildId, timelineId, recordId, type, name, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling AttachmentsApi.AttachmentsGetWithHttpInfo: " + e.Message);
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
| **timelineId** | **Guid** | The ID of the timeline. |  |
| **recordId** | **Guid** | The ID of the timeline record. |  |
| **type** | **string** | The type of the attachment. |  |
| **name** | **string** | The name of the attachment. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.2&#39; to use this version of the api. |  |

### Return type

**string**

### Authorization

[oauth2](../README.md#oauth2)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/octet-stream


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

<a name="attachmentslist"></a>
# **AttachmentsList**
> List&lt;Attachment&gt; AttachmentsList (string organization, string project, int buildId, string type, string apiVersion)



Gets the list of attachments of a specific type that are associated with a build.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class AttachmentsListExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new AttachmentsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var type = "type_example";  // string | The type of attachment.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.2' to use this version of the api.

            try
            {
                List<Attachment> result = apiInstance.AttachmentsList(organization, project, buildId, type, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling AttachmentsApi.AttachmentsList: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the AttachmentsListWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<Attachment>> response = apiInstance.AttachmentsListWithHttpInfo(organization, project, buildId, type, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling AttachmentsApi.AttachmentsListWithHttpInfo: " + e.Message);
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
| **type** | **string** | The type of attachment. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.2&#39; to use this version of the api. |  |

### Return type

[**List&lt;Attachment&gt;**](Attachment.md)

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

