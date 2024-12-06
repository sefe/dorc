# Org.OpenAPITools.Api.FoldersApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**FoldersCreate**](FoldersApi.md#folderscreate) | **PUT** /{organization}/{project}/_apis/build/folders |  |
| [**FoldersDelete**](FoldersApi.md#foldersdelete) | **DELETE** /{organization}/{project}/_apis/build/folders |  |
| [**FoldersList**](FoldersApi.md#folderslist) | **GET** /{organization}/{project}/_apis/build/folders/{path} |  |
| [**FoldersUpdate**](FoldersApi.md#foldersupdate) | **POST** /{organization}/{project}/_apis/build/folders |  |

<a name="folderscreate"></a>
# **FoldersCreate**
> Folder FoldersCreate (string organization, string project, string path, string apiVersion, Folder body)



Creates a new folder.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class FoldersCreateExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new FoldersApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var path = "path_example";  // string | The full path of the folder.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.2' to use this version of the api.
            var body = new Folder(); // Folder | The folder.

            try
            {
                Folder result = apiInstance.FoldersCreate(organization, project, path, apiVersion, body);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling FoldersApi.FoldersCreate: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the FoldersCreateWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<Folder> response = apiInstance.FoldersCreateWithHttpInfo(organization, project, path, apiVersion, body);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling FoldersApi.FoldersCreateWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **path** | **string** | The full path of the folder. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.2&#39; to use this version of the api. |  |
| **body** | [**Folder**](Folder.md) | The folder. |  |

### Return type

[**Folder**](Folder.md)

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

<a name="foldersdelete"></a>
# **FoldersDelete**
> void FoldersDelete (string organization, string project, string path, string apiVersion)



Deletes a definition folder. Definitions and their corresponding builds will also be deleted.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class FoldersDeleteExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new FoldersApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var path = "path_example";  // string | The full path to the folder.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.2' to use this version of the api.

            try
            {
                apiInstance.FoldersDelete(organization, project, path, apiVersion);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling FoldersApi.FoldersDelete: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the FoldersDeleteWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    apiInstance.FoldersDeleteWithHttpInfo(organization, project, path, apiVersion);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling FoldersApi.FoldersDeleteWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **path** | **string** | The full path to the folder. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.2&#39; to use this version of the api. |  |

### Return type

void (empty response body)

### Authorization

[oauth2](../README.md#oauth2)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: Not defined


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

<a name="folderslist"></a>
# **FoldersList**
> List&lt;Folder&gt; FoldersList (string organization, string project, string path, string apiVersion, string? queryOrder = null)



Gets a list of build definition folders.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class FoldersListExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new FoldersApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var path = "path_example";  // string | The path to start with.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.2' to use this version of the api.
            var queryOrder = "none";  // string? | The order in which folders should be returned. (optional) 

            try
            {
                List<Folder> result = apiInstance.FoldersList(organization, project, path, apiVersion, queryOrder);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling FoldersApi.FoldersList: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the FoldersListWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<Folder>> response = apiInstance.FoldersListWithHttpInfo(organization, project, path, apiVersion, queryOrder);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling FoldersApi.FoldersListWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **path** | **string** | The path to start with. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.2&#39; to use this version of the api. |  |
| **queryOrder** | **string?** | The order in which folders should be returned. | [optional]  |

### Return type

[**List&lt;Folder&gt;**](Folder.md)

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

<a name="foldersupdate"></a>
# **FoldersUpdate**
> Folder FoldersUpdate (string organization, string project, string path, string apiVersion, Folder body)



Updates an existing folder at given  existing path

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class FoldersUpdateExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new FoldersApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var path = "path_example";  // string | The full path to the folder.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.2' to use this version of the api.
            var body = new Folder(); // Folder | The new version of the folder.

            try
            {
                Folder result = apiInstance.FoldersUpdate(organization, project, path, apiVersion, body);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling FoldersApi.FoldersUpdate: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the FoldersUpdateWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<Folder> response = apiInstance.FoldersUpdateWithHttpInfo(organization, project, path, apiVersion, body);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling FoldersApi.FoldersUpdateWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **path** | **string** | The full path to the folder. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.2&#39; to use this version of the api. |  |
| **body** | [**Folder**](Folder.md) | The new version of the folder. |  |

### Return type

[**Folder**](Folder.md)

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

