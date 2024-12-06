# Org.OpenAPITools.Api.LeasesApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**LeasesAdd**](LeasesApi.md#leasesadd) | **POST** /{organization}/{project}/_apis/build/retention/leases |  |
| [**LeasesDelete**](LeasesApi.md#leasesdelete) | **DELETE** /{organization}/{project}/_apis/build/retention/leases |  |
| [**LeasesGet**](LeasesApi.md#leasesget) | **GET** /{organization}/{project}/_apis/build/retention/leases/{leaseId} |  |
| [**LeasesGetRetentionLeasesByMinimalRetentionLeases**](LeasesApi.md#leasesgetretentionleasesbyminimalretentionleases) | **GET** /{organization}/{project}/_apis/build/retention/leases |  |

<a name="leasesadd"></a>
# **LeasesAdd**
> List&lt;RetentionLease&gt; LeasesAdd (string organization, string project, string apiVersion, List<NewRetentionLease> body)



Adds new leases for pipeline runs.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class LeasesAddExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new LeasesApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var body = new List<NewRetentionLease>(); // List<NewRetentionLease> | 

            try
            {
                List<RetentionLease> result = apiInstance.LeasesAdd(organization, project, apiVersion, body);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling LeasesApi.LeasesAdd: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the LeasesAddWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<RetentionLease>> response = apiInstance.LeasesAddWithHttpInfo(organization, project, apiVersion, body);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling LeasesApi.LeasesAddWithHttpInfo: " + e.Message);
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
| **body** | [**List&lt;NewRetentionLease&gt;**](NewRetentionLease.md) |  |  |

### Return type

[**List&lt;RetentionLease&gt;**](RetentionLease.md)

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

<a name="leasesdelete"></a>
# **LeasesDelete**
> void LeasesDelete (string organization, string project, string ids, string apiVersion)



Removes specific retention leases.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class LeasesDeleteExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new LeasesApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var ids = "ids_example";  // string | 
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.

            try
            {
                apiInstance.LeasesDelete(organization, project, ids, apiVersion);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling LeasesApi.LeasesDelete: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the LeasesDeleteWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    apiInstance.LeasesDeleteWithHttpInfo(organization, project, ids, apiVersion);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling LeasesApi.LeasesDeleteWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **ids** | **string** |  |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |

### Return type

void (empty response body)

### Authorization

[accessToken](../README.md#accessToken)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: Not defined


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

<a name="leasesget"></a>
# **LeasesGet**
> RetentionLease LeasesGet (string organization, string project, int leaseId, string apiVersion)



Returns the details of the retention lease given a lease id.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class LeasesGetExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new LeasesApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var leaseId = 56;  // int | 
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.

            try
            {
                RetentionLease result = apiInstance.LeasesGet(organization, project, leaseId, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling LeasesApi.LeasesGet: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the LeasesGetWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<RetentionLease> response = apiInstance.LeasesGetWithHttpInfo(organization, project, leaseId, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling LeasesApi.LeasesGetWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **leaseId** | **int** |  |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |

### Return type

[**RetentionLease**](RetentionLease.md)

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

<a name="leasesgetretentionleasesbyminimalretentionleases"></a>
# **LeasesGetRetentionLeasesByMinimalRetentionLeases**
> List&lt;RetentionLease&gt; LeasesGetRetentionLeasesByMinimalRetentionLeases (string organization, string project, string leasesToFetch, string apiVersion)



Returns any leases matching the specified MinimalRetentionLeases

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class LeasesGetRetentionLeasesByMinimalRetentionLeasesExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new LeasesApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var leasesToFetch = "leasesToFetch_example";  // string | List of JSON-serialized MinimalRetentionLeases separated by '|'
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.

            try
            {
                List<RetentionLease> result = apiInstance.LeasesGetRetentionLeasesByMinimalRetentionLeases(organization, project, leasesToFetch, apiVersion);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling LeasesApi.LeasesGetRetentionLeasesByMinimalRetentionLeases: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the LeasesGetRetentionLeasesByMinimalRetentionLeasesWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<RetentionLease>> response = apiInstance.LeasesGetRetentionLeasesByMinimalRetentionLeasesWithHttpInfo(organization, project, leasesToFetch, apiVersion);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling LeasesApi.LeasesGetRetentionLeasesByMinimalRetentionLeasesWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **leasesToFetch** | **string** | List of JSON-serialized MinimalRetentionLeases separated by &#39;|&#39; |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |

### Return type

[**List&lt;RetentionLease&gt;**](RetentionLease.md)

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

