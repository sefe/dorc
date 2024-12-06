# Org.OpenAPITools.Model.DataSourceBindingBase
Represents binding of data source for the service endpoint request.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**CallbackContextTemplate** | **string** | Pagination format supported by this data source(ContinuationToken/SkipTop). | [optional] 
**CallbackRequiredTemplate** | **string** | Subsequent calls needed? | [optional] 
**DataSourceName** | **string** | Gets or sets the name of the data source. | [optional] 
**EndpointId** | **string** | Gets or sets the endpoint Id. | [optional] 
**EndpointUrl** | **string** | Gets or sets the url of the service endpoint. | [optional] 
**Headers** | [**List&lt;AuthorizationHeader&gt;**](AuthorizationHeader.md) | Gets or sets the authorization headers. | [optional] 
**InitialContextTemplate** | **string** | Defines the initial value of the query params | [optional] 
**Parameters** | **Dictionary&lt;string, string&gt;** | Gets or sets the parameters for the data source. | [optional] 
**RequestContent** | **string** | Gets or sets http request body | [optional] 
**RequestVerb** | **string** | Gets or sets http request verb | [optional] 
**ResultSelector** | **string** | Gets or sets the result selector. | [optional] 
**ResultTemplate** | **string** | Gets or sets the result template. | [optional] 
**Target** | **string** | Gets or sets the target of the data source. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

