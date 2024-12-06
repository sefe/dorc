# Org.OpenAPITools.Api.DefinitionsApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**DefinitionsCreate**](DefinitionsApi.md#definitionscreate) | **POST** /{organization}/{project}/_apis/build/definitions |  |
| [**DefinitionsDelete**](DefinitionsApi.md#definitionsdelete) | **DELETE** /{organization}/{project}/_apis/build/definitions/{definitionId} |  |
| [**DefinitionsGet**](DefinitionsApi.md#definitionsget) | **GET** /{organization}/{project}/_apis/build/definitions/{definitionId} |  |
| [**DefinitionsGetDefinitionRevisions**](DefinitionsApi.md#definitionsgetdefinitionrevisions) | **GET** /{organization}/{project}/_apis/build/definitions/{definitionId}/revisions |  |
| [**DefinitionsList**](DefinitionsApi.md#definitionslist) | **GET** /{organization}/{project}/_apis/build/definitions |  |
| [**DefinitionsRestoreDefinition**](DefinitionsApi.md#definitionsrestoredefinition) | **PATCH** /{organization}/{project}/_apis/build/definitions/{definitionId} |  |
| [**DefinitionsUpdate**](DefinitionsApi.md#definitionsupdate) | **PUT** /{organization}/{project}/_apis/build/definitions/{definitionId} |  |

<a name="definitionscreate"></a>
# **DefinitionsCreate**
> BuildDefinition DefinitionsCreate (string organization, string project, string apiVersion, BuildDefinition body, int? definitionToCloneId = null, int? definitionToCloneRevision = null)



Creates a new definition.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class DefinitionsCreateExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new DefinitionsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var body = new BuildDefinition(); // BuildDefinition | The definition.
            var definitionToCloneId = 56;  // int? |  (optional) 
            var definitionToCloneRevision = 56;  // int? |  (optional) 

            try
            {
                BuildDefinition result = apiInstance.DefinitionsCreate(organization, project, apiVersion, body, definitionToCloneId, definitionToCloneRevision);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling DefinitionsApi.DefinitionsCreate: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the DefinitionsCreateWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<BuildDefinition> response = apiInstance.DefinitionsCreateWithHttpInfo(organization, project, apiVersion, body, definitionToCloneId, definitionToCloneRevision);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling DefinitionsApi.DefinitionsCreateWithHttpInfo: " + e.Message);
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
| **body** | [**BuildDefinition**](BuildDefinition.md) | The definition. |  |
| **definitionToCloneId** | **int?** |  | [optional]  |
| **definitionToCloneRevision** | **int?** |  | [optional]  |

### Return type

[**BuildDefinition**](BuildDefinition.md)

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

<a name="definitionsdelete"></a>
# **DefinitionsDelete**
> void DefinitionsDelete (string organization, string project, int definitionId, string apiVersion)



Deletes a definition and all associated builds.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class DefinitionsDeleteExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new DefinitionsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definitionId = 56;  // int | The ID of the definition.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.

            try
            {
                apiInstance.DefinitionsDelete(organization, project, definitionId, apiVersion);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling DefinitionsApi.DefinitionsDelete: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the DefinitionsDeleteWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    apiInstance.DefinitionsDeleteWithHttpInfo(organization, project, definitionId, apiVersion);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling DefinitionsApi.DefinitionsDeleteWithHttpInfo: " + e.Message);
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

<a name="definitionsget"></a>
# **DefinitionsGet**
> BuildDefinition DefinitionsGet (string organization, string project, int definitionId, string apiVersion, int? revision = null, DateTime? minMetricsTime = null, string? propertyFilters = null, bool? includeLatestBuilds = null)



Gets a definition, optionally at a specific revision.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class DefinitionsGetExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new DefinitionsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definitionId = 56;  // int | The ID of the definition.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var revision = 56;  // int? | The revision number to retrieve. If this is not specified, the latest version will be returned. (optional) 
            var minMetricsTime = DateTime.Parse("2013-10-20T19:20:30+01:00");  // DateTime? | If specified, indicates the date from which metrics should be included. (optional) 
            var propertyFilters = "propertyFilters_example";  // string? | A comma-delimited list of properties to include in the results. (optional) 
            var includeLatestBuilds = true;  // bool? |  (optional) 

            try
            {
                BuildDefinition result = apiInstance.DefinitionsGet(organization, project, definitionId, apiVersion, revision, minMetricsTime, propertyFilters, includeLatestBuilds);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling DefinitionsApi.DefinitionsGet: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the DefinitionsGetWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<BuildDefinition> response = apiInstance.DefinitionsGetWithHttpInfo(organization, project, definitionId, apiVersion, revision, minMetricsTime, propertyFilters, includeLatestBuilds);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling DefinitionsApi.DefinitionsGetWithHttpInfo: " + e.Message);
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
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |
| **revision** | **int?** | The revision number to retrieve. If this is not specified, the latest version will be returned. | [optional]  |
| **minMetricsTime** | **DateTime?** | If specified, indicates the date from which metrics should be included. | [optional]  |
| **propertyFilters** | **string?** | A comma-delimited list of properties to include in the results. | [optional]  |
| **includeLatestBuilds** | **bool?** |  | [optional]  |

### Return type

[**BuildDefinition**](BuildDefinition.md)

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

<a name="definitionsgetdefinitionrevisions"></a>
# **DefinitionsGetDefinitionRevisions**
> List&lt;BuildDefinitionRevision&gt; DefinitionsGetDefinitionRevisions (string organization, string project, int definitionId, string apiVersion)



Gets all revisions of a definition.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class DefinitionsGetDefinitionRevisionsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new DefinitionsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definitionId = 56;  // int | The ID of the definition.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.

            try
            {
                List<BuildDefinitionRevision> result = apiInstance.DefinitionsGetDefinitionRevisions(organization, project, definitionId, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling DefinitionsApi.DefinitionsGetDefinitionRevisions: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the DefinitionsGetDefinitionRevisionsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<BuildDefinitionRevision>> response = apiInstance.DefinitionsGetDefinitionRevisionsWithHttpInfo(organization, project, definitionId, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling DefinitionsApi.DefinitionsGetDefinitionRevisionsWithHttpInfo: " + e.Message);
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
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |

### Return type

[**List&lt;BuildDefinitionRevision&gt;**](BuildDefinitionRevision.md)

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

<a name="definitionslist"></a>
# **DefinitionsList**
> List&lt;BuildDefinitionReference&gt; DefinitionsList (string organization, string project, string apiVersion, string? name = null, string? repositoryId = null, string? repositoryType = null, string? queryOrder = null, int? top = null, string? continuationToken = null, DateTime? minMetricsTime = null, string? definitionIds = null, string? path = null, DateTime? builtAfter = null, DateTime? notBuiltAfter = null, bool? includeAllProperties = null, bool? includeLatestBuilds = null, Guid? taskIdFilter = null, int? processType = null, string? yamlFilename = null)



Gets a list of definitions.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class DefinitionsListExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new DefinitionsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var name = "name_example";  // string? | If specified, filters to definitions whose names match this pattern. (optional) 
            var repositoryId = "repositoryId_example";  // string? | A repository ID. If specified, filters to definitions that use this repository. (optional) 
            var repositoryType = "repositoryType_example";  // string? | If specified, filters to definitions that have a repository of this type. (optional) 
            var queryOrder = "none";  // string? | Indicates the order in which definitions should be returned. (optional) 
            var top = 56;  // int? | The maximum number of definitions to return. (optional) 
            var continuationToken = "continuationToken_example";  // string? | A continuation token, returned by a previous call to this method, that can be used to return the next set of definitions. (optional) 
            var minMetricsTime = DateTime.Parse("2013-10-20T19:20:30+01:00");  // DateTime? | If specified, indicates the date from which metrics should be included. (optional) 
            var definitionIds = "definitionIds_example";  // string? | A comma-delimited list that specifies the IDs of definitions to retrieve. (optional) 
            var path = "path_example";  // string? | If specified, filters to definitions under this folder. (optional) 
            var builtAfter = DateTime.Parse("2013-10-20T19:20:30+01:00");  // DateTime? | If specified, filters to definitions that have builds after this date. (optional) 
            var notBuiltAfter = DateTime.Parse("2013-10-20T19:20:30+01:00");  // DateTime? | If specified, filters to definitions that do not have builds after this date. (optional) 
            var includeAllProperties = true;  // bool? | Indicates whether the full definitions should be returned. By default, shallow representations of the definitions are returned. (optional) 
            var includeLatestBuilds = true;  // bool? | Indicates whether to return the latest and latest completed builds for this definition. (optional) 
            var taskIdFilter = "taskIdFilter_example";  // Guid? | If specified, filters to definitions that use the specified task. (optional) 
            var processType = 56;  // int? | If specified, filters to definitions with the given process type. (optional) 
            var yamlFilename = "yamlFilename_example";  // string? | If specified, filters to YAML definitions that match the given filename. (optional) 

            try
            {
                List<BuildDefinitionReference> result = apiInstance.DefinitionsList(organization, project, apiVersion, name, repositoryId, repositoryType, queryOrder, top, continuationToken, minMetricsTime, definitionIds, path, builtAfter, notBuiltAfter, includeAllProperties, includeLatestBuilds, taskIdFilter, processType, yamlFilename);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling DefinitionsApi.DefinitionsList: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the DefinitionsListWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<BuildDefinitionReference>> response = apiInstance.DefinitionsListWithHttpInfo(organization, project, apiVersion, name, repositoryId, repositoryType, queryOrder, top, continuationToken, minMetricsTime, definitionIds, path, builtAfter, notBuiltAfter, includeAllProperties, includeLatestBuilds, taskIdFilter, processType, yamlFilename);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling DefinitionsApi.DefinitionsListWithHttpInfo: " + e.Message);
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
| **name** | **string?** | If specified, filters to definitions whose names match this pattern. | [optional]  |
| **repositoryId** | **string?** | A repository ID. If specified, filters to definitions that use this repository. | [optional]  |
| **repositoryType** | **string?** | If specified, filters to definitions that have a repository of this type. | [optional]  |
| **queryOrder** | **string?** | Indicates the order in which definitions should be returned. | [optional]  |
| **top** | **int?** | The maximum number of definitions to return. | [optional]  |
| **continuationToken** | **string?** | A continuation token, returned by a previous call to this method, that can be used to return the next set of definitions. | [optional]  |
| **minMetricsTime** | **DateTime?** | If specified, indicates the date from which metrics should be included. | [optional]  |
| **definitionIds** | **string?** | A comma-delimited list that specifies the IDs of definitions to retrieve. | [optional]  |
| **path** | **string?** | If specified, filters to definitions under this folder. | [optional]  |
| **builtAfter** | **DateTime?** | If specified, filters to definitions that have builds after this date. | [optional]  |
| **notBuiltAfter** | **DateTime?** | If specified, filters to definitions that do not have builds after this date. | [optional]  |
| **includeAllProperties** | **bool?** | Indicates whether the full definitions should be returned. By default, shallow representations of the definitions are returned. | [optional]  |
| **includeLatestBuilds** | **bool?** | Indicates whether to return the latest and latest completed builds for this definition. | [optional]  |
| **taskIdFilter** | **Guid?** | If specified, filters to definitions that use the specified task. | [optional]  |
| **processType** | **int?** | If specified, filters to definitions with the given process type. | [optional]  |
| **yamlFilename** | **string?** | If specified, filters to YAML definitions that match the given filename. | [optional]  |

### Return type

[**List&lt;BuildDefinitionReference&gt;**](BuildDefinitionReference.md)

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

<a name="definitionsrestoredefinition"></a>
# **DefinitionsRestoreDefinition**
> BuildDefinition DefinitionsRestoreDefinition (string organization, string project, int definitionId, bool deleted, string apiVersion)



Restores a deleted definition

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class DefinitionsRestoreDefinitionExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new DefinitionsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definitionId = 56;  // int | The identifier of the definition to restore.
            var deleted = true;  // bool | When false, restores a deleted definition.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.

            try
            {
                BuildDefinition result = apiInstance.DefinitionsRestoreDefinition(organization, project, definitionId, deleted, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling DefinitionsApi.DefinitionsRestoreDefinition: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the DefinitionsRestoreDefinitionWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<BuildDefinition> response = apiInstance.DefinitionsRestoreDefinitionWithHttpInfo(organization, project, definitionId, deleted, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling DefinitionsApi.DefinitionsRestoreDefinitionWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **definitionId** | **int** | The identifier of the definition to restore. |  |
| **deleted** | **bool** | When false, restores a deleted definition. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |

### Return type

[**BuildDefinition**](BuildDefinition.md)

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

<a name="definitionsupdate"></a>
# **DefinitionsUpdate**
> BuildDefinition DefinitionsUpdate (string organization, string project, int definitionId, string apiVersion, BuildDefinition body, int? secretsSourceDefinitionId = null, int? secretsSourceDefinitionRevision = null)



Updates an existing definition.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class DefinitionsUpdateExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new DefinitionsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definitionId = 56;  // int | The ID of the definition.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0' to use this version of the api.
            var body = new BuildDefinition(); // BuildDefinition | The new version of the definition.
            var secretsSourceDefinitionId = 56;  // int? |  (optional) 
            var secretsSourceDefinitionRevision = 56;  // int? |  (optional) 

            try
            {
                BuildDefinition result = apiInstance.DefinitionsUpdate(organization, project, definitionId, apiVersion, body, secretsSourceDefinitionId, secretsSourceDefinitionRevision);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling DefinitionsApi.DefinitionsUpdate: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the DefinitionsUpdateWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<BuildDefinition> response = apiInstance.DefinitionsUpdateWithHttpInfo(organization, project, definitionId, apiVersion, body, secretsSourceDefinitionId, secretsSourceDefinitionRevision);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling DefinitionsApi.DefinitionsUpdateWithHttpInfo: " + e.Message);
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
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0&#39; to use this version of the api. |  |
| **body** | [**BuildDefinition**](BuildDefinition.md) | The new version of the definition. |  |
| **secretsSourceDefinitionId** | **int?** |  | [optional]  |
| **secretsSourceDefinitionRevision** | **int?** |  | [optional]  |

### Return type

[**BuildDefinition**](BuildDefinition.md)

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

