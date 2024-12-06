# Org.OpenAPITools.Model.BuildDefinitionTemplate
Represents a template from which new build definitions can be created.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**CanDelete** | **bool** | Indicates whether the template can be deleted. | [optional] 
**Category** | **string** | The template category. | [optional] 
**DefaultHostedQueue** | **string** | An optional hosted agent queue for the template to use by default. | [optional] 
**Description** | **string** | A description of the template. | [optional] 
**Icons** | **Dictionary&lt;string, string&gt;** |  | [optional] 
**IconTaskId** | **Guid** | The ID of the task whose icon is used when showing this template in the UI. | [optional] 
**Id** | **string** | The ID of the template. | [optional] 
**Name** | **string** | The name of the template. | [optional] 
**Template** | [**BuildDefinition**](BuildDefinition.md) |  | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

