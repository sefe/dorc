# Org.OpenAPITools.Api.MetricsApi

All URIs are relative to *https://dev.azure.com*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**MetricsGetDefinitionMetrics**](MetricsApi.md#metricsgetdefinitionmetrics) | **GET** /{organization}/{project}/_apis/build/definitions/{definitionId}/metrics |  |
| [**MetricsGetProjectMetrics**](MetricsApi.md#metricsgetprojectmetrics) | **GET** /{organization}/{project}/_apis/build/metrics/{metricAggregationType} |  |

<a name="metricsgetdefinitionmetrics"></a>
# **MetricsGetDefinitionMetrics**
> List&lt;BuildMetric&gt; MetricsGetDefinitionMetrics (string organization, string project, int definitionId, string apiVersion, DateTime? minMetricsTime = null)



Gets build metrics for a definition.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class MetricsGetDefinitionMetricsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure OAuth2 access token for authorization: oauth2
            config.AccessToken = "YOUR_ACCESS_TOKEN";

            var apiInstance = new MetricsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var definitionId = 56;  // int | The ID of the definition.
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var minMetricsTime = DateTime.Parse("2013-10-20T19:20:30+01:00");  // DateTime? | The date from which to calculate metrics. (optional) 

            try
            {
                List<BuildMetric> result = apiInstance.MetricsGetDefinitionMetrics(organization, project, definitionId, apiVersion, minMetricsTime);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling MetricsApi.MetricsGetDefinitionMetrics: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the MetricsGetDefinitionMetricsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<BuildMetric>> response = apiInstance.MetricsGetDefinitionMetricsWithHttpInfo(organization, project, definitionId, apiVersion, minMetricsTime);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling MetricsApi.MetricsGetDefinitionMetricsWithHttpInfo: " + e.Message);
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
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **minMetricsTime** | **DateTime?** | The date from which to calculate metrics. | [optional]  |

### Return type

[**List&lt;BuildMetric&gt;**](BuildMetric.md)

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

<a name="metricsgetprojectmetrics"></a>
# **MetricsGetProjectMetrics**
> List&lt;BuildMetric&gt; MetricsGetProjectMetrics (string organization, string project, string metricAggregationType, string apiVersion, DateTime? minMetricsTime = null)



Gets build metrics for a project.

### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;

namespace Example
{
    public class MetricsGetProjectMetricsExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://dev.azure.com";
            // Configure HTTP basic authorization: accessToken
            config.Username = "YOUR_USERNAME";
            config.Password = "YOUR_PASSWORD";

            var apiInstance = new MetricsApi(config);
            var organization = "organization_example";  // string | The name of the Azure DevOps organization.
            var project = "project_example";  // string | Project ID or project name
            var metricAggregationType = "metricAggregationType_example";  // string | The aggregation type to use (hourly, daily).
            var apiVersion = "apiVersion_example";  // string | Version of the API to use.  This should be set to '6.0-preview.1' to use this version of the api.
            var minMetricsTime = DateTime.Parse("2013-10-20T19:20:30+01:00");  // DateTime? | The date from which to calculate metrics. (optional) 

            try
            {
                List<BuildMetric> result = apiInstance.MetricsGetProjectMetrics(organization, project, metricAggregationType, apiVersion, minMetricsTime);
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling MetricsApi.MetricsGetProjectMetrics: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the MetricsGetProjectMetricsWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<List<BuildMetric>> response = apiInstance.MetricsGetProjectMetricsWithHttpInfo(organization, project, metricAggregationType, apiVersion, minMetricsTime);
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling MetricsApi.MetricsGetProjectMetricsWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **organization** | **string** | The name of the Azure DevOps organization. |  |
| **project** | **string** | Project ID or project name |  |
| **metricAggregationType** | **string** | The aggregation type to use (hourly, daily). |  |
| **apiVersion** | **string** | Version of the API to use.  This should be set to &#39;6.0-preview.1&#39; to use this version of the api. |  |
| **minMetricsTime** | **DateTime?** | The date from which to calculate metrics. | [optional]  |

### Return type

[**List&lt;BuildMetric&gt;**](BuildMetric.md)

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

