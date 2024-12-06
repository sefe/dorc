# Org.OpenAPITools.Api.TagsApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**TagsAddBuildTag**](TagsApi.md#tagsaddbuildtag) | **PUT** /{organization}/{project}/_apis/build/builds/{buildId}/tags/{tag} |  |
| [**TagsAddBuildTags**](TagsApi.md#tagsaddbuildtags) | **POST** /{organization}/{project}/_apis/build/builds/{buildId}/tags |  |
| [**TagsAddDefinitionTag**](TagsApi.md#tagsadddefinitiontag) | **PUT** /{organization}/{project}/_apis/build/definitions/{DefinitionId}/tags/{tag} |  |
| [**TagsAddDefinitionTags**](TagsApi.md#tagsadddefinitiontags) | **POST** /{organization}/{project}/_apis/build/definitions/{DefinitionId}/tags |  |
| [**TagsDeleteBuildTag**](TagsApi.md#tagsdeletebuildtag) | **DELETE** /{organization}/{project}/_apis/build/builds/{buildId}/tags/{tag} |  |
| [**TagsDeleteDefinitionTag**](TagsApi.md#tagsdeletedefinitiontag) | **DELETE** /{organization}/{project}/_apis/build/definitions/{DefinitionId}/tags/{tag} |  |
| [**TagsDeleteTag**](TagsApi.md#tagsdeletetag) | **DELETE** /{organization}/{project}/_apis/build/tags/{tag} |  |
| [**TagsGetBuildTags**](TagsApi.md#tagsgetbuildtags) | **GET** /{organization}/{project}/_apis/build/builds/{buildId}/tags |  |
| [**TagsGetDefinitionTags**](TagsApi.md#tagsgetdefinitiontags) | **GET** /{organization}/{project}/_apis/build/definitions/{DefinitionId}/tags |  |
| [**TagsGetTags**](TagsApi.md#tagsgettags) | **GET** /{organization}/{project}/_apis/build/tags |  |

<a name="tagsaddbuildtag"></a>
# **TagsAddBuildTag**
> List&lt;string&gt; TagsAddBuildTag (string organization, string project, int buildId, string tag, string apiVersion)



Adds a tag to a build.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class TagsAddBuildTagExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new TagsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var tag = "tag_example";  // string | The tag to add.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.

            try
            {
                List<string> result = apiInstance.TagsAddBuildTag(organization, project, buildId, tag, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling TagsApi.TagsAddBuildTag: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the TagsAddBuildTagWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<string>> response = apiInstance.TagsAddBuildTagWithHttpInfo(organization, project, buildId, tag, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling TagsApi.TagsAddBuildTagWithHttpInfo: " + e.Message);
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
| **tag** | **string** | The tag to add. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |

### Return type

**List<string>**

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

<a name="tagsaddbuildtags"></a>
# **TagsAddBuildTags**
> List&lt;string&gt; TagsAddBuildTags (string organization, string project, int buildId, string apiVersion, List<string> body)



Adds tags to a build.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class TagsAddBuildTagsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new TagsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var body = new List<string>(); // List<string> | The tags to add.

            try
            {
                List<string> result = apiInstance.TagsAddBuildTags(organization, project, buildId, apiVersion, body);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling TagsApi.TagsAddBuildTags: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the TagsAddBuildTagsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<string>> response = apiInstance.TagsAddBuildTagsWithHttpInfo(organization, project, buildId, apiVersion, body);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling TagsApi.TagsAddBuildTagsWithHttpInfo: " + e.Message);
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
| **body** | [**List&lt;string&gt;**](string.md) | The tags to add. |  |

### Return type

**List<string>**

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

<a name="tagsadddefinitiontag"></a>
# **TagsAddDefinitionTag**
> List&lt;string&gt; TagsAddDefinitionTag (string organization, string project, int definitionId, string tag, string apiVersion)



Adds a tag to a definition

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class TagsAddDefinitionTagExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new TagsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definitionId = 56;  // int | The ID of the definition.
            var tag = "tag_example";  // string | The tag to add.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.2' to use this version of the api.

            try
            {
                List<string> result = apiInstance.TagsAddDefinitionTag(organization, project, definitionId, tag, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling TagsApi.TagsAddDefinitionTag: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the TagsAddDefinitionTagWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<string>> response = apiInstance.TagsAddDefinitionTagWithHttpInfo(organization, project, definitionId, tag, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling TagsApi.TagsAddDefinitionTagWithHttpInfo: " + e.Message);
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
| **tag** | **string** | The tag to add. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.2&#39; to use this version of the api. |  |

### Return type

**List<string>**

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

<a name="tagsadddefinitiontags"></a>
# **TagsAddDefinitionTags**
> List&lt;string&gt; TagsAddDefinitionTags (string organization, string project, int definitionId, string apiVersion, List<string> body)



Adds multiple tags to a definition.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class TagsAddDefinitionTagsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new TagsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definitionId = 56;  // int | The ID of the definition.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.2' to use this version of the api.
            var body = new List<string>(); // List<string> | The tags to add.

            try
            {
                List<string> result = apiInstance.TagsAddDefinitionTags(organization, project, definitionId, apiVersion, body);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling TagsApi.TagsAddDefinitionTags: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the TagsAddDefinitionTagsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<string>> response = apiInstance.TagsAddDefinitionTagsWithHttpInfo(organization, project, definitionId, apiVersion, body);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling TagsApi.TagsAddDefinitionTagsWithHttpInfo: " + e.Message);
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
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.2&#39; to use this version of the api. |  |
| **body** | [**List&lt;string&gt;**](string.md) | The tags to add. |  |

### Return type

**List<string>**

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

<a name="tagsdeletebuildtag"></a>
# **TagsDeleteBuildTag**
> List&lt;string&gt; TagsDeleteBuildTag (string organization, string project, int buildId, string tag, string apiVersion)



Removes a tag from a build.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class TagsDeleteBuildTagExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new TagsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var tag = "tag_example";  // string | The tag to remove.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.

            try
            {
                List<string> result = apiInstance.TagsDeleteBuildTag(organization, project, buildId, tag, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling TagsApi.TagsDeleteBuildTag: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the TagsDeleteBuildTagWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<string>> response = apiInstance.TagsDeleteBuildTagWithHttpInfo(organization, project, buildId, tag, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling TagsApi.TagsDeleteBuildTagWithHttpInfo: " + e.Message);
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
| **tag** | **string** | The tag to remove. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |

### Return type

**List<string>**

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

<a name="tagsdeletedefinitiontag"></a>
# **TagsDeleteDefinitionTag**
> List&lt;string&gt; TagsDeleteDefinitionTag (string organization, string project, int definitionId, string tag, string apiVersion)



Removes a tag from a definition.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class TagsDeleteDefinitionTagExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new TagsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definitionId = 56;  // int | The ID of the definition.
            var tag = "tag_example";  // string | The tag to remove.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.2' to use this version of the api.

            try
            {
                List<string> result = apiInstance.TagsDeleteDefinitionTag(organization, project, definitionId, tag, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling TagsApi.TagsDeleteDefinitionTag: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the TagsDeleteDefinitionTagWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<string>> response = apiInstance.TagsDeleteDefinitionTagWithHttpInfo(organization, project, definitionId, tag, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling TagsApi.TagsDeleteDefinitionTagWithHttpInfo: " + e.Message);
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
| **tag** | **string** | The tag to remove. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.2&#39; to use this version of the api. |  |

### Return type

**List<string>**

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

<a name="tagsdeletetag"></a>
# **TagsDeleteTag**
> List&lt;string&gt; TagsDeleteTag (string organization, string project, string tag, string apiVersion)



Removes a tag from builds, definitions, and from the tag store

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class TagsDeleteTagExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new TagsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var tag = "tag_example";  // string | The tag to remove.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.

            try
            {
                List<string> result = apiInstance.TagsDeleteTag(organization, project, tag, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling TagsApi.TagsDeleteTag: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the TagsDeleteTagWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<string>> response = apiInstance.TagsDeleteTagWithHttpInfo(organization, project, tag, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling TagsApi.TagsDeleteTagWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **tag** | **string** | The tag to remove. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |

### Return type

**List<string>**

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

<a name="tagsgetbuildtags"></a>
# **TagsGetBuildTags**
> List&lt;string&gt; TagsGetBuildTags (string organization, string project, int buildId, string apiVersion)



Gets the tags for a build.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class TagsGetBuildTagsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new TagsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var buildId = 56;  // int | The ID of the build.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.

            try
            {
                List<string> result = apiInstance.TagsGetBuildTags(organization, project, buildId, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling TagsApi.TagsGetBuildTags: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the TagsGetBuildTagsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<string>> response = apiInstance.TagsGetBuildTagsWithHttpInfo(organization, project, buildId, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling TagsApi.TagsGetBuildTagsWithHttpInfo: " + e.Message);
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

**List<string>**

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

<a name="tagsgetdefinitiontags"></a>
# **TagsGetDefinitionTags**
> List&lt;string&gt; TagsGetDefinitionTags (string organization, string project, int definitionId, string apiVersion, int? revision = null)



Gets the tags for a definition.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class TagsGetDefinitionTagsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new TagsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definitionId = 56;  // int | The ID of the definition.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.2' to use this version of the api.
            var revision = 56;  // int? | The definition revision number. If not specified, uses the latest revision of the definition. (optional) 

            try
            {
                List<string> result = apiInstance.TagsGetDefinitionTags(organization, project, definitionId, apiVersion, revision);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling TagsApi.TagsGetDefinitionTags: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the TagsGetDefinitionTagsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<string>> response = apiInstance.TagsGetDefinitionTagsWithHttpInfo(organization, project, definitionId, apiVersion, revision);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling TagsApi.TagsGetDefinitionTagsWithHttpInfo: " + e.Message);
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
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.2&#39; to use this version of the api. |  |
| **revision** | **int?** | The definition revision number. If not specified, uses the latest revision of the definition. | [optional]  |

### Return type

**List<string>**

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

<a name="tagsgettags"></a>
# **TagsGetTags**
> List&lt;string&gt; TagsGetTags (string organization, string project, string apiVersion)



Gets a list of all build tags in the project.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class TagsGetTagsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new TagsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.

            try
            {
                List<string> result = apiInstance.TagsGetTags(organization, project, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling TagsApi.TagsGetTags: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the TagsGetTagsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<string>> response = apiInstance.TagsGetTagsWithHttpInfo(organization, project, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling TagsApi.TagsGetTagsWithHttpInfo: " + e.Message);
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

**List<string>**

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

