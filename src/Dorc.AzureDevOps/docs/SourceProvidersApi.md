# Org.OpenAPITools.Api.SourceProvidersApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**SourceProvidersGetFileContents**](SourceProvidersApi.md#sourceprovidersgetfilecontents) | **GET** /{organization}/{project}/_apis/sourceProviders/{providerName}/filecontents |  |
| [**SourceProvidersGetPathContents**](SourceProvidersApi.md#sourceprovidersgetpathcontents) | **GET** /{organization}/{project}/_apis/sourceProviders/{providerName}/pathcontents |  |
| [**SourceProvidersGetPullRequest**](SourceProvidersApi.md#sourceprovidersgetpullrequest) | **GET** /{organization}/{project}/_apis/sourceProviders/{providerName}/pullrequests/{pullRequestId} |  |
| [**SourceProvidersList**](SourceProvidersApi.md#sourceproviderslist) | **GET** /{organization}/{project}/_apis/sourceproviders |  |
| [**SourceProvidersListBranches**](SourceProvidersApi.md#sourceproviderslistbranches) | **GET** /{organization}/{project}/_apis/sourceProviders/{providerName}/branches |  |
| [**SourceProvidersListRepositories**](SourceProvidersApi.md#sourceproviderslistrepositories) | **GET** /{organization}/{project}/_apis/sourceProviders/{providerName}/repositories |  |
| [**SourceProvidersListWebhooks**](SourceProvidersApi.md#sourceproviderslistwebhooks) | **GET** /{organization}/{project}/_apis/sourceProviders/{providerName}/webhooks |  |
| [**SourceProvidersRestoreWebhooks**](SourceProvidersApi.md#sourceprovidersrestorewebhooks) | **POST** /{organization}/{project}/_apis/sourceProviders/{providerName}/webhooks |  |

<a name="sourceprovidersgetfilecontents"></a>
# **SourceProvidersGetFileContents**
> string SourceProvidersGetFileContents (string organization, string project, string providerName, string apiVersion, Guid? serviceEndpointId = null, string? repository = null, string? commitOrBranch = null, string? path = null)



Gets the contents of a file in the given source code repository.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class SourceProvidersGetFileContentsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new SourceProvidersApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var providerName = "providerName_example";  // string | The name of the source provider.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var serviceEndpointId = "serviceEndpointId_example";  // Guid? | If specified, the ID of the service endpoint to query. Can only be omitted for providers that do not use service endpoints, e.g. TFVC or TFGit. (optional) 
            var repository = "repository_example";  // string? | If specified, the vendor-specific identifier or the name of the repository to get branches. Can only be omitted for providers that do not support multiple repositories. (optional) 
            var commitOrBranch = "commitOrBranch_example";  // string? | The identifier of the commit or branch from which a file's contents are retrieved. (optional) 
            var path = "path_example";  // string? | The path to the file to retrieve, relative to the root of the repository. (optional) 

            try
            {
                string result = apiInstance.SourceProvidersGetFileContents(organization, project, providerName, apiVersion, serviceEndpointId, repository, commitOrBranch, path);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersGetFileContents: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the SourceProvidersGetFileContentsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<string> response = apiInstance.SourceProvidersGetFileContentsWithHttpInfo(organization, project, providerName, apiVersion, serviceEndpointId, repository, commitOrBranch, path);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersGetFileContentsWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **providerName** | **string** | The name of the source provider. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **serviceEndpointId** | **Guid?** | If specified, the ID of the service endpoint to query. Can only be omitted for providers that do not use service endpoints, e.g. TFVC or TFGit. | [optional]  |
| **repository** | **string?** | If specified, the vendor-specific identifier or the name of the repository to get branches. Can only be omitted for providers that do not support multiple repositories. | [optional]  |
| **commitOrBranch** | **string?** | The identifier of the commit or branch from which a file&#39;s contents are retrieved. | [optional]  |
| **path** | **string?** | The path to the file to retrieve, relative to the root of the repository. | [optional]  |

### Return type

**string**

### Authorization

[accessToken](../README.md#accessToken)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: text/plain


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

<a name="sourceprovidersgetpathcontents"></a>
# **SourceProvidersGetPathContents**
> List&lt;SourceRepositoryItem&gt; SourceProvidersGetPathContents (string organization, string project, string providerName, string apiVersion, Guid? serviceEndpointId = null, string? repository = null, string? commitOrBranch = null, string? path = null)



Gets the contents of a directory in the given source code repository.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class SourceProvidersGetPathContentsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new SourceProvidersApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var providerName = "providerName_example";  // string | The name of the source provider.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var serviceEndpointId = "serviceEndpointId_example";  // Guid? | If specified, the ID of the service endpoint to query. Can only be omitted for providers that do not use service endpoints, e.g. TFVC or TFGit. (optional) 
            var repository = "repository_example";  // string? | If specified, the vendor-specific identifier or the name of the repository to get branches. Can only be omitted for providers that do not support multiple repositories. (optional) 
            var commitOrBranch = "commitOrBranch_example";  // string? | The identifier of the commit or branch from which a file's contents are retrieved. (optional) 
            var path = "path_example";  // string? | The path contents to list, relative to the root of the repository. (optional) 

            try
            {
                List<SourceRepositoryItem> result = apiInstance.SourceProvidersGetPathContents(organization, project, providerName, apiVersion, serviceEndpointId, repository, commitOrBranch, path);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersGetPathContents: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the SourceProvidersGetPathContentsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<SourceRepositoryItem>> response = apiInstance.SourceProvidersGetPathContentsWithHttpInfo(organization, project, providerName, apiVersion, serviceEndpointId, repository, commitOrBranch, path);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersGetPathContentsWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **providerName** | **string** | The name of the source provider. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **serviceEndpointId** | **Guid?** | If specified, the ID of the service endpoint to query. Can only be omitted for providers that do not use service endpoints, e.g. TFVC or TFGit. | [optional]  |
| **repository** | **string?** | If specified, the vendor-specific identifier or the name of the repository to get branches. Can only be omitted for providers that do not support multiple repositories. | [optional]  |
| **commitOrBranch** | **string?** | The identifier of the commit or branch from which a file&#39;s contents are retrieved. | [optional]  |
| **path** | **string?** | The path contents to list, relative to the root of the repository. | [optional]  |

### Return type

[**List&lt;SourceRepositoryItem&gt;**](SourceRepositoryItem.md)

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

<a name="sourceprovidersgetpullrequest"></a>
# **SourceProvidersGetPullRequest**
> PullRequest SourceProvidersGetPullRequest (string organization, string project, string providerName, string pullRequestId, string apiVersion, string? repositoryId = null, Guid? serviceEndpointId = null)



Gets a pull request object from source provider.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class SourceProvidersGetPullRequestExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new SourceProvidersApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var providerName = "providerName_example";  // string | The name of the source provider.
            var pullRequestId = "pullRequestId_example";  // string | Vendor-specific id of the pull request.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var repositoryId = "repositoryId_example";  // string? | Vendor-specific identifier or the name of the repository that contains the pull request. (optional) 
            var serviceEndpointId = "serviceEndpointId_example";  // Guid? | If specified, the ID of the service endpoint to query. Can only be omitted for providers that do not use service endpoints, e.g. TFVC or TFGit. (optional) 

            try
            {
                PullRequest result = apiInstance.SourceProvidersGetPullRequest(organization, project, providerName, pullRequestId, apiVersion, repositoryId, serviceEndpointId);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersGetPullRequest: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the SourceProvidersGetPullRequestWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<PullRequest> response = apiInstance.SourceProvidersGetPullRequestWithHttpInfo(organization, project, providerName, pullRequestId, apiVersion, repositoryId, serviceEndpointId);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersGetPullRequestWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **providerName** | **string** | The name of the source provider. |  |
| **pullRequestId** | **string** | Vendor-specific id of the pull request. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **repositoryId** | **string?** | Vendor-specific identifier or the name of the repository that contains the pull request. | [optional]  |
| **serviceEndpointId** | **Guid?** | If specified, the ID of the service endpoint to query. Can only be omitted for providers that do not use service endpoints, e.g. TFVC or TFGit. | [optional]  |

### Return type

[**PullRequest**](PullRequest.md)

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

<a name="sourceproviderslist"></a>
# **SourceProvidersList**
> List&lt;SourceProviderAttributes&gt; SourceProvidersList (string organization, string project, string apiVersion)



Get a list of source providers and their capabilities.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class SourceProvidersListExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new SourceProvidersApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.

            try
            {
                List<SourceProviderAttributes> result = apiInstance.SourceProvidersList(organization, project, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersList: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the SourceProvidersListWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<SourceProviderAttributes>> response = apiInstance.SourceProvidersListWithHttpInfo(organization, project, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersListWithHttpInfo: " + e.Message);
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

[**List&lt;SourceProviderAttributes&gt;**](SourceProviderAttributes.md)

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

<a name="sourceproviderslistbranches"></a>
# **SourceProvidersListBranches**
> List&lt;string&gt; SourceProvidersListBranches (string organization, string project, string providerName, string apiVersion, Guid? serviceEndpointId = null, string? repository = null, string? branchName = null)



Gets a list of branches for the given source code repository.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class SourceProvidersListBranchesExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new SourceProvidersApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var providerName = "providerName_example";  // string | The name of the source provider.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var serviceEndpointId = "serviceEndpointId_example";  // Guid? | If specified, the ID of the service endpoint to query. Can only be omitted for providers that do not use service endpoints, e.g. TFVC or TFGit. (optional) 
            var repository = "repository_example";  // string? | The vendor-specific identifier or the name of the repository to get branches. Can only be omitted for providers that do not support multiple repositories. (optional) 
            var branchName = "branchName_example";  // string? | If supplied, the name of the branch to check for specifically. (optional) 

            try
            {
                List<string> result = apiInstance.SourceProvidersListBranches(organization, project, providerName, apiVersion, serviceEndpointId, repository, branchName);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersListBranches: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the SourceProvidersListBranchesWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<string>> response = apiInstance.SourceProvidersListBranchesWithHttpInfo(organization, project, providerName, apiVersion, serviceEndpointId, repository, branchName);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersListBranchesWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **providerName** | **string** | The name of the source provider. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **serviceEndpointId** | **Guid?** | If specified, the ID of the service endpoint to query. Can only be omitted for providers that do not use service endpoints, e.g. TFVC or TFGit. | [optional]  |
| **repository** | **string?** | The vendor-specific identifier or the name of the repository to get branches. Can only be omitted for providers that do not support multiple repositories. | [optional]  |
| **branchName** | **string?** | If supplied, the name of the branch to check for specifically. | [optional]  |

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

<a name="sourceproviderslistrepositories"></a>
# **SourceProvidersListRepositories**
> SourceRepositories SourceProvidersListRepositories (string organization, string project, string providerName, string apiVersion, Guid? serviceEndpointId = null, string? repository = null, string? resultSet = null, bool? pageResults = null, string? continuationToken = null)



Gets a list of source code repositories.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class SourceProvidersListRepositoriesExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new SourceProvidersApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var providerName = "providerName_example";  // string | The name of the source provider.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var serviceEndpointId = "serviceEndpointId_example";  // Guid? | If specified, the ID of the service endpoint to query. Can only be omitted for providers that do not use service endpoints, e.g. TFVC or TFGit. (optional) 
            var repository = "repository_example";  // string? | If specified, the vendor-specific identifier or the name of a single repository to get. (optional) 
            var resultSet = "all";  // string? | 'top' for the repositories most relevant for the endpoint. If not set, all repositories are returned. Ignored if 'repository' is set. (optional) 
            var pageResults = true;  // bool? | If set to true, this will limit the set of results and will return a continuation token to continue the query. (optional) 
            var continuationToken = "continuationToken_example";  // string? | When paging results, this is a continuation token, returned by a previous call to this method, that can be used to return the next set of repositories. (optional) 

            try
            {
                SourceRepositories result = apiInstance.SourceProvidersListRepositories(organization, project, providerName, apiVersion, serviceEndpointId, repository, resultSet, pageResults, continuationToken);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersListRepositories: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the SourceProvidersListRepositoriesWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<SourceRepositories> response = apiInstance.SourceProvidersListRepositoriesWithHttpInfo(organization, project, providerName, apiVersion, serviceEndpointId, repository, resultSet, pageResults, continuationToken);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersListRepositoriesWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **providerName** | **string** | The name of the source provider. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **serviceEndpointId** | **Guid?** | If specified, the ID of the service endpoint to query. Can only be omitted for providers that do not use service endpoints, e.g. TFVC or TFGit. | [optional]  |
| **repository** | **string?** | If specified, the vendor-specific identifier or the name of a single repository to get. | [optional]  |
| **resultSet** | **string?** | &#39;top&#39; for the repositories most relevant for the endpoint. If not set, all repositories are returned. Ignored if &#39;repository&#39; is set. | [optional]  |
| **pageResults** | **bool?** | If set to true, this will limit the set of results and will return a continuation token to continue the query. | [optional]  |
| **continuationToken** | **string?** | When paging results, this is a continuation token, returned by a previous call to this method, that can be used to return the next set of repositories. | [optional]  |

### Return type

[**SourceRepositories**](SourceRepositories.md)

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

<a name="sourceproviderslistwebhooks"></a>
# **SourceProvidersListWebhooks**
> List&lt;RepositoryWebhook&gt; SourceProvidersListWebhooks (string organization, string project, string providerName, string apiVersion, Guid? serviceEndpointId = null, string? repository = null)



Gets a list of webhooks installed in the given source code repository.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class SourceProvidersListWebhooksExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new SourceProvidersApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var providerName = "providerName_example";  // string | The name of the source provider.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var serviceEndpointId = "serviceEndpointId_example";  // Guid? | If specified, the ID of the service endpoint to query. Can only be omitted for providers that do not use service endpoints, e.g. TFVC or TFGit. (optional) 
            var repository = "repository_example";  // string? | If specified, the vendor-specific identifier or the name of the repository to get webhooks. Can only be omitted for providers that do not support multiple repositories. (optional) 

            try
            {
                List<RepositoryWebhook> result = apiInstance.SourceProvidersListWebhooks(organization, project, providerName, apiVersion, serviceEndpointId, repository);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersListWebhooks: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the SourceProvidersListWebhooksWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<RepositoryWebhook>> response = apiInstance.SourceProvidersListWebhooksWithHttpInfo(organization, project, providerName, apiVersion, serviceEndpointId, repository);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersListWebhooksWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **providerName** | **string** | The name of the source provider. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **serviceEndpointId** | **Guid?** | If specified, the ID of the service endpoint to query. Can only be omitted for providers that do not use service endpoints, e.g. TFVC or TFGit. | [optional]  |
| **repository** | **string?** | If specified, the vendor-specific identifier or the name of the repository to get webhooks. Can only be omitted for providers that do not support multiple repositories. | [optional]  |

### Return type

[**List&lt;RepositoryWebhook&gt;**](RepositoryWebhook.md)

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

<a name="sourceprovidersrestorewebhooks"></a>
# **SourceProvidersRestoreWebhooks**
> void SourceProvidersRestoreWebhooks (string organization, string project, string providerName, string apiVersion, List<string> body, Guid? serviceEndpointId = null, string? repository = null)



Recreates the webhooks for the specified triggers in the given source code repository.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class SourceProvidersRestoreWebhooksExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new SourceProvidersApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var providerName = "providerName_example";  // string | The name of the source provider.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var body = new List<string>(); // List<string> | The types of triggers to restore webhooks for.
            var serviceEndpointId = "serviceEndpointId_example";  // Guid? | If specified, the ID of the service endpoint to query. Can only be omitted for providers that do not use service endpoints, e.g. TFVC or TFGit. (optional) 
            var repository = "repository_example";  // string? | If specified, the vendor-specific identifier or the name of the repository to get webhooks. Can only be omitted for providers that do not support multiple repositories. (optional) 

            try
            {
                apiInstance.SourceProvidersRestoreWebhooks(organization, project, providerName, apiVersion, body, serviceEndpointId, repository);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersRestoreWebhooks: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the SourceProvidersRestoreWebhooksWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    apiInstance.SourceProvidersRestoreWebhooksWithHttpInfo(organization, project, providerName, apiVersion, body, serviceEndpointId, repository);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling SourceProvidersApi.SourceProvidersRestoreWebhooksWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **providerName** | **string** | The name of the source provider. |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **body** | [**List&lt;string&gt;**](string.md) | The types of triggers to restore webhooks for. |  |
| **serviceEndpointId** | **Guid?** | If specified, the ID of the service endpoint to query. Can only be omitted for providers that do not use service endpoints, e.g. TFVC or TFGit. | [optional]  |
| **repository** | **string?** | If specified, the vendor-specific identifier or the name of the repository to get webhooks. Can only be omitted for providers that do not support multiple repositories. | [optional]  |

### Return type

void (empty response body)

### Authorization

[accessToken](../README.md#accessToken)

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: Not defined


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

