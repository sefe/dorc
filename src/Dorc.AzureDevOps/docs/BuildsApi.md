# Org.OpenAPITools.Api.BuildsApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**BuildsDelete**](BuildsApi.md#buildsdelete) | **DELETE** /{organization}/{project}/_apis/build/builds/{buildId} |  |
| [**BuildsGet**](BuildsApi.md#buildsget) | **GET** /{organization}/{project}/_apis/build/builds/{buildId} |  |
| [**BuildsGetBuildChanges**](BuildsApi.md#buildsgetbuildchanges) | **GET** /{organization}/{project}/_apis/build/builds/{buildId}/changes |  |
| [**BuildsGetBuildLog**](BuildsApi.md#buildsgetbuildlog) | **GET** /{organization}/{project}/_apis/build/builds/{buildId}/logs/{logId} |  |
| [**BuildsGetBuildLogs**](BuildsApi.md#buildsgetbuildlogs) | **GET** /{organization}/{project}/_apis/build/builds/{buildId}/logs |  |
| [**BuildsGetBuildWorkItemsRefs**](BuildsApi.md#buildsgetbuildworkitemsrefs) | **GET** /{organization}/{project}/_apis/build/builds/{buildId}/workitems |  |
| [**BuildsGetBuildWorkItemsRefsFromCommits**](BuildsApi.md#buildsgetbuildworkitemsrefsfromcommits) | **POST** /{organization}/{project}/_apis/build/builds/{buildId}/workitems |  |
| [**BuildsGetChangesBetweenBuilds**](BuildsApi.md#buildsgetchangesbetweenbuilds) | **GET** /{organization}/{project}/_apis/build/changes |  |
| [**BuildsGetWorkItemsBetweenBuilds**](BuildsApi.md#buildsgetworkitemsbetweenbuilds) | **GET** /{organization}/{project}/_apis/build/workitems |  |
| [**BuildsList**](BuildsApi.md#buildslist) | **GET** /{organization}/{project}/_apis/build/builds |  |
| [**BuildsQueue**](BuildsApi.md#buildsqueue) | **POST** /{organization}/{project}/_apis/build/builds |  |
| [**BuildsUpdateBuild**](BuildsApi.md#buildsupdatebuild) | **PATCH** /{organization}/{project}/_apis/build/builds/{buildId} |  |
| [**BuildsUpdateBuilds**](BuildsApi.md#buildsupdatebuilds) | **PATCH** /{organization}/{project}/_apis/build/builds |  |

<a name="buildsdelete"></a>
# **BuildsDelete**
> void BuildsDelete (string organization, string project, int buildId, string apiVersion)



Deletes a build.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BuildsDeleteExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new BuildsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.

            try
            {
                apiInstance.BuildsDelete(organization, project, buildId, apiVersion);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BuildsApi.BuildsDelete: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BuildsDeleteWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    apiInstance.BuildsDeleteWithHttpInfo(organization, project, buildId, apiVersion);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BuildsApi.BuildsDeleteWithHttpInfo: " + e.Message);
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
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |

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

<a name="buildsget"></a>
# **BuildsGet**
> Build BuildsGet (string organization, string project, int buildId, string apiVersion, string? propertyFilters = null)



Gets a build

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BuildsGetExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new BuildsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | 
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var propertyFilters = "propertyFilters_example";  // string? |  (optional) 

            try
            {
                Build result = apiInstance.BuildsGet(organization, project, buildId, apiVersion, propertyFilters);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BuildsApi.BuildsGet: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BuildsGetWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<Build> response = apiInstance.BuildsGetWithHttpInfo(organization, project, buildId, apiVersion, propertyFilters);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BuildsApi.BuildsGetWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **buildId** | **int** |  |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |
| **propertyFilters** | **string?** |  | [optional]  |

### Return type

[**Build**](Build.md)

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

<a name="buildsgetbuildchanges"></a>
# **BuildsGetBuildChanges**
> List&lt;Change&gt; BuildsGetBuildChanges (string organization, string project, int buildId, string apiVersion, string? continuationToken = null, int? top = null, bool? includeSourceChange = null)



Gets the changes associated with a build

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BuildsGetBuildChangesExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new BuildsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | 
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var continuationToken = "continuationToken_example";  // string? |  (optional) 
            var top = 56;  // int? | The maximum number of changes to return (optional) 
            var includeSourceChange = true;  // bool? |  (optional) 

            try
            {
                List<Change> result = apiInstance.BuildsGetBuildChanges(organization, project, buildId, apiVersion, continuationToken, top, includeSourceChange);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BuildsApi.BuildsGetBuildChanges: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BuildsGetBuildChangesWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<Change>> response = apiInstance.BuildsGetBuildChangesWithHttpInfo(organization, project, buildId, apiVersion, continuationToken, top, includeSourceChange);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BuildsApi.BuildsGetBuildChangesWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **buildId** | **int** |  |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |
| **continuationToken** | **string?** |  | [optional]  |
| **top** | **int?** | The maximum number of changes to return | [optional]  |
| **includeSourceChange** | **bool?** |  | [optional]  |

### Return type

[**List&lt;Change&gt;**](Change.md)

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

<a name="buildsgetbuildlog"></a>
# **BuildsGetBuildLog**
> string BuildsGetBuildLog (string organization, string project, int buildId, int logId, string apiVersion, long? startLine = null, long? endLine = null)



Gets an individual log file for a build.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BuildsGetBuildLogExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new BuildsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var logId = 56;  // int | The ID of the log file.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var startLine = 789L;  // long? | The start line. (optional) 
            var endLine = 789L;  // long? | The end line. (optional) 

            try
            {
                string result = apiInstance.BuildsGetBuildLog(organization, project, buildId, logId, apiVersion, startLine, endLine);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BuildsApi.BuildsGetBuildLog: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BuildsGetBuildLogWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<string> response = apiInstance.BuildsGetBuildLogWithHttpInfo(organization, project, buildId, logId, apiVersion, startLine, endLine);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BuildsApi.BuildsGetBuildLogWithHttpInfo: " + e.Message);
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
| **logId** | **int** | The ID of the log file. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |
| **startLine** | **long?** | The start line. | [optional]  |
| **endLine** | **long?** | The end line. | [optional]  |

### Return type

**string**

### Authorization

[oauth2](../README.md#oauth2)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/zip, application/json, text/plain


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

<a name="buildsgetbuildlogs"></a>
# **BuildsGetBuildLogs**
> List&lt;BuildLog&gt; BuildsGetBuildLogs (string organization, string project, int buildId, string apiVersion)



Gets the logs for a build.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BuildsGetBuildLogsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new BuildsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.

            try
            {
                List<BuildLog> result = apiInstance.BuildsGetBuildLogs(organization, project, buildId, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BuildsApi.BuildsGetBuildLogs: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BuildsGetBuildLogsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<BuildLog>> response = apiInstance.BuildsGetBuildLogsWithHttpInfo(organization, project, buildId, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BuildsApi.BuildsGetBuildLogsWithHttpInfo: " + e.Message);
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
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |

### Return type

[**List&lt;BuildLog&gt;**](BuildLog.md)

### Authorization

[oauth2](../README.md#oauth2)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/zip, application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

<a name="buildsgetbuildworkitemsrefs"></a>
# **BuildsGetBuildWorkItemsRefs**
> List&lt;ResourceRef&gt; BuildsGetBuildWorkItemsRefs (string organization, string project, int buildId, string apiVersion, int? top = null)



Gets the work items associated with a build.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BuildsGetBuildWorkItemsRefsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new BuildsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var top = 56;  // int? | The maximum number of work items to return. (optional) 

            try
            {
                List<ResourceRef> result = apiInstance.BuildsGetBuildWorkItemsRefs(organization, project, buildId, apiVersion, top);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BuildsApi.BuildsGetBuildWorkItemsRefs: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BuildsGetBuildWorkItemsRefsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<ResourceRef>> response = apiInstance.BuildsGetBuildWorkItemsRefsWithHttpInfo(organization, project, buildId, apiVersion, top);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BuildsApi.BuildsGetBuildWorkItemsRefsWithHttpInfo: " + e.Message);
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
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |
| **top** | **int?** | The maximum number of work items to return. | [optional]  |

### Return type

[**List&lt;ResourceRef&gt;**](ResourceRef.md)

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

<a name="buildsgetbuildworkitemsrefsfromcommits"></a>
# **BuildsGetBuildWorkItemsRefsFromCommits**
> List&lt;ResourceRef&gt; BuildsGetBuildWorkItemsRefsFromCommits (string organization, string project, int buildId, string apiVersion, List<string> body, int? top = null)



Gets the work items associated with a build, filtered to specific commits.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BuildsGetBuildWorkItemsRefsFromCommitsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new BuildsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var body = new List<string>(); // List<string> | A comma-delimited list of commit IDs.
            var top = 56;  // int? | The maximum number of work items to return, or the number of commits to consider if no commit IDs are specified. (optional) 

            try
            {
                List<ResourceRef> result = apiInstance.BuildsGetBuildWorkItemsRefsFromCommits(organization, project, buildId, apiVersion, body, top);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BuildsApi.BuildsGetBuildWorkItemsRefsFromCommits: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BuildsGetBuildWorkItemsRefsFromCommitsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<ResourceRef>> response = apiInstance.BuildsGetBuildWorkItemsRefsFromCommitsWithHttpInfo(organization, project, buildId, apiVersion, body, top);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BuildsApi.BuildsGetBuildWorkItemsRefsFromCommitsWithHttpInfo: " + e.Message);
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
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |
| **body** | [**List&lt;string&gt;**](string.md) | A comma-delimited list of commit IDs. |  |
| **top** | **int?** | The maximum number of work items to return, or the number of commits to consider if no commit IDs are specified. | [optional]  |

### Return type

[**List&lt;ResourceRef&gt;**](ResourceRef.md)

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

<a name="buildsgetchangesbetweenbuilds"></a>
# **BuildsGetChangesBetweenBuilds**
> List&lt;Change&gt; BuildsGetChangesBetweenBuilds (string organization, string project, string apiVersion, int? fromBuildId = null, int? toBuildId = null, int? top = null)



Gets the changes made to the repository between two given builds.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BuildsGetChangesBetweenBuildsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new BuildsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.2' to use this version of the api.
            var fromBuildId = 56;  // int? | The ID of the first build. (optional) 
            var toBuildId = 56;  // int? | The ID of the last build. (optional) 
            var top = 56;  // int? | The maximum number of changes to return. (optional) 

            try
            {
                List<Change> result = apiInstance.BuildsGetChangesBetweenBuilds(organization, project, apiVersion, fromBuildId, toBuildId, top);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BuildsApi.BuildsGetChangesBetweenBuilds: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BuildsGetChangesBetweenBuildsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<Change>> response = apiInstance.BuildsGetChangesBetweenBuildsWithHttpInfo(organization, project, apiVersion, fromBuildId, toBuildId, top);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BuildsApi.BuildsGetChangesBetweenBuildsWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.2&#39; to use this version of the api. |  |
| **fromBuildId** | **int?** | The ID of the first build. | [optional]  |
| **toBuildId** | **int?** | The ID of the last build. | [optional]  |
| **top** | **int?** | The maximum number of changes to return. | [optional]  |

### Return type

[**List&lt;Change&gt;**](Change.md)

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

<a name="buildsgetworkitemsbetweenbuilds"></a>
# **BuildsGetWorkItemsBetweenBuilds**
> List&lt;ResourceRef&gt; BuildsGetWorkItemsBetweenBuilds (string organization, string project, int fromBuildId, int toBuildId, string apiVersion, int? top = null)



Gets all the work items between two builds.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BuildsGetWorkItemsBetweenBuildsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new BuildsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var fromBuildId = 56;  // int | The ID of the first build.
            var toBuildId = 56;  // int | The ID of the last build.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.2' to use this version of the api.
            var top = 56;  // int? | The maximum number of work items to return. (optional) 

            try
            {
                List<ResourceRef> result = apiInstance.BuildsGetWorkItemsBetweenBuilds(organization, project, fromBuildId, toBuildId, apiVersion, top);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BuildsApi.BuildsGetWorkItemsBetweenBuilds: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BuildsGetWorkItemsBetweenBuildsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<ResourceRef>> response = apiInstance.BuildsGetWorkItemsBetweenBuildsWithHttpInfo(organization, project, fromBuildId, toBuildId, apiVersion, top);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BuildsApi.BuildsGetWorkItemsBetweenBuildsWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **fromBuildId** | **int** | The ID of the first build. |  |
| **toBuildId** | **int** | The ID of the last build. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.2&#39; to use this version of the api. |  |
| **top** | **int?** | The maximum number of work items to return. | [optional]  |

### Return type

[**List&lt;ResourceRef&gt;**](ResourceRef.md)

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

<a name="buildslist"></a>
# **BuildsList**
> List&lt;Build&gt; BuildsList (string organization, string project, string apiVersion, string? definitions = null, string? queues = null, string? buildNumber = null, DateTime? minTime = null, DateTime? maxTime = null, string? requestedFor = null, string? reasonFilter = null, string? statusFilter = null, string? resultFilter = null, string? tagFilters = null, string? properties = null, int? top = null, string? continuationToken = null, int? maxBuildsPerDefinition = null, string? deletedFilter = null, string? queryOrder = null, string? branchName = null, string? buildIds = null, string? repositoryId = null, string? repositoryType = null)



Gets a list of builds.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BuildsListExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new BuildsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var definitions = "definitions_example";  // string? | A comma-delimited list of definition IDs. If specified, filters to builds for these definitions. (optional) 
            var queues = "queues_example";  // string? | A comma-delimited list of queue IDs. If specified, filters to builds that ran against these queues. (optional) 
            var buildNumber = "buildNumber_example";  // string? | If specified, filters to builds that match this build number. Append * to do a prefix search. (optional) 
            var minTime = DateTime.Parse("2013-10-20T19:20:30+01:00");  // DateTime? | If specified, filters to builds that finished/started/queued after this date based on the queryOrder specified. (optional) 
            var maxTime = DateTime.Parse("2013-10-20T19:20:30+01:00");  // DateTime? | If specified, filters to builds that finished/started/queued before this date based on the queryOrder specified. (optional) 
            var requestedFor = "requestedFor_example";  // string? | If specified, filters to builds requested for the specified user. (optional) 
            var reasonFilter = "none";  // string? | If specified, filters to builds that match this reason. (optional) 
            var statusFilter = "none";  // string? | If specified, filters to builds that match this status. (optional) 
            var resultFilter = "none";  // string? | If specified, filters to builds that match this result. (optional) 
            var tagFilters = "tagFilters_example";  // string? | A comma-delimited list of tags. If specified, filters to builds that have the specified tags. (optional) 
            var properties = "properties_example";  // string? | A comma-delimited list of properties to retrieve. (optional) 
            var top = 56;  // int? | The maximum number of builds to return. (optional) 
            var continuationToken = "continuationToken_example";  // string? | A continuation token, returned by a previous call to this method, that can be used to return the next set of builds. (optional) 
            var maxBuildsPerDefinition = 56;  // int? | The maximum number of builds to return per definition. (optional) 
            var deletedFilter = "excludeDeleted";  // string? | Indicates whether to exclude, include, or only return deleted builds. (optional) 
            var queryOrder = "finishTimeAscending";  // string? | The order in which builds should be returned. (optional) 
            var branchName = "branchName_example";  // string? | If specified, filters to builds that built branches that built this branch. (optional) 
            var buildIds = "buildIds_example";  // string? | A comma-delimited list that specifies the IDs of builds to retrieve. (optional) 
            var repositoryId = "repositoryId_example";  // string? | If specified, filters to builds that built from this repository. (optional) 
            var repositoryType = "repositoryType_example";  // string? | If specified, filters to builds that built from repositories of this type. (optional) 

            try
            {
                List<Build> result = apiInstance.BuildsList(organization, project, apiVersion, definitions, queues, buildNumber, minTime, maxTime, requestedFor, reasonFilter, statusFilter, resultFilter, tagFilters, properties, top, continuationToken, maxBuildsPerDefinition, deletedFilter, queryOrder, branchName, buildIds, repositoryId, repositoryType);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BuildsApi.BuildsList: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BuildsListWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<Build>> response = apiInstance.BuildsListWithHttpInfo(organization, project, apiVersion, definitions, queues, buildNumber, minTime, maxTime, requestedFor, reasonFilter, statusFilter, resultFilter, tagFilters, properties, top, continuationToken, maxBuildsPerDefinition, deletedFilter, queryOrder, branchName, buildIds, repositoryId, repositoryType);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BuildsApi.BuildsListWithHttpInfo: " + e.Message);
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
| **definitions** | **string?** | A comma-delimited list of definition IDs. If specified, filters to builds for these definitions. | [optional]  |
| **queues** | **string?** | A comma-delimited list of queue IDs. If specified, filters to builds that ran against these queues. | [optional]  |
| **buildNumber** | **string?** | If specified, filters to builds that match this build number. Append * to do a prefix search. | [optional]  |
| **minTime** | **DateTime?** | If specified, filters to builds that finished/started/queued after this date based on the queryOrder specified. | [optional]  |
| **maxTime** | **DateTime?** | If specified, filters to builds that finished/started/queued before this date based on the queryOrder specified. | [optional]  |
| **requestedFor** | **string?** | If specified, filters to builds requested for the specified user. | [optional]  |
| **reasonFilter** | **string?** | If specified, filters to builds that match this reason. | [optional]  |
| **statusFilter** | **string?** | If specified, filters to builds that match this status. | [optional]  |
| **resultFilter** | **string?** | If specified, filters to builds that match this result. | [optional]  |
| **tagFilters** | **string?** | A comma-delimited list of tags. If specified, filters to builds that have the specified tags. | [optional]  |
| **properties** | **string?** | A comma-delimited list of properties to retrieve. | [optional]  |
| **top** | **int?** | The maximum number of builds to return. | [optional]  |
| **continuationToken** | **string?** | A continuation token, returned by a previous call to this method, that can be used to return the next set of builds. | [optional]  |
| **maxBuildsPerDefinition** | **int?** | The maximum number of builds to return per definition. | [optional]  |
| **deletedFilter** | **string?** | Indicates whether to exclude, include, or only return deleted builds. | [optional]  |
| **queryOrder** | **string?** | The order in which builds should be returned. | [optional]  |
| **branchName** | **string?** | If specified, filters to builds that built branches that built this branch. | [optional]  |
| **buildIds** | **string?** | A comma-delimited list that specifies the IDs of builds to retrieve. | [optional]  |
| **repositoryId** | **string?** | If specified, filters to builds that built from this repository. | [optional]  |
| **repositoryType** | **string?** | If specified, filters to builds that built from repositories of this type. | [optional]  |

### Return type

[**List&lt;Build&gt;**](Build.md)

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

<a name="buildsqueue"></a>
# **BuildsQueue**
> Build BuildsQueue (string organization, string project, string apiVersion, Build body, bool? ignoreWarnings = null, string? checkInTicket = null, int? sourceBuildId = null, int? definitionId = null)



Queues a build

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BuildsQueueExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new BuildsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var body = new Build(); // Build | 
            var ignoreWarnings = true;  // bool? |  (optional) 
            var checkInTicket = "checkInTicket_example";  // string? |  (optional) 
            var sourceBuildId = 56;  // int? |  (optional) 
            var definitionId = 56;  // int? | Optional definition id to queue a build without a body. Ignored if there's a valid body (optional) 

            try
            {
                Build result = apiInstance.BuildsQueue(organization, project, apiVersion, body, ignoreWarnings, checkInTicket, sourceBuildId, definitionId);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BuildsApi.BuildsQueue: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BuildsQueueWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<Build> response = apiInstance.BuildsQueueWithHttpInfo(organization, project, apiVersion, body, ignoreWarnings, checkInTicket, sourceBuildId, definitionId);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BuildsApi.BuildsQueueWithHttpInfo: " + e.Message);
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
| **body** | [**Build**](Build.md) |  |  |
| **ignoreWarnings** | **bool?** |  | [optional]  |
| **checkInTicket** | **string?** |  | [optional]  |
| **sourceBuildId** | **int?** |  | [optional]  |
| **definitionId** | **int?** | Optional definition id to queue a build without a body. Ignored if there&#39;s a valid body | [optional]  |

### Return type

[**Build**](Build.md)

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

<a name="buildsupdatebuild"></a>
# **BuildsUpdateBuild**
> Build BuildsUpdateBuild (string organization, string project, int buildId, string apiVersion, Build body, bool? retry = null)



Updates a build.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BuildsUpdateBuildExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new BuildsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var body = new Build(); // Build | The build.
            var retry = true;  // bool? |  (optional) 

            try
            {
                Build result = apiInstance.BuildsUpdateBuild(organization, project, buildId, apiVersion, body, retry);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BuildsApi.BuildsUpdateBuild: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BuildsUpdateBuildWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<Build> response = apiInstance.BuildsUpdateBuildWithHttpInfo(organization, project, buildId, apiVersion, body, retry);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BuildsApi.BuildsUpdateBuildWithHttpInfo: " + e.Message);
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
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |
| **body** | [**Build**](Build.md) | The build. |  |
| **retry** | **bool?** |  | [optional]  |

### Return type

[**Build**](Build.md)

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

<a name="buildsupdatebuilds"></a>
# **BuildsUpdateBuilds**
> List&lt;Build&gt; BuildsUpdateBuilds (string organization, string project, string apiVersion, List<Build> body)



Updates multiple builds.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class BuildsUpdateBuildsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new BuildsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var body = new List<Build>(); // List<Build> | The builds to update.

            try
            {
                List<Build> result = apiInstance.BuildsUpdateBuilds(organization, project, apiVersion, body);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling BuildsApi.BuildsUpdateBuilds: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the BuildsUpdateBuildsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<Build>> response = apiInstance.BuildsUpdateBuildsWithHttpInfo(organization, project, apiVersion, body);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling BuildsApi.BuildsUpdateBuildsWithHttpInfo: " + e.Message);
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
| **body** | [**List&lt;Build&gt;**](Build.md) | The builds to update. |  |

### Return type

[**List&lt;Build&gt;**](Build.md)

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

