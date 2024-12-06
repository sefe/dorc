# Org.OpenAPITools.Model.SourceRelatedWorkItem
Represents a work item related to some source item. These are retrieved from Source Providers.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**Links** | [**ReferenceLinks**](ReferenceLinks.md) |  | [optional] 
**AssignedTo** | [**IdentityRef**](IdentityRef.md) |  | [optional] 
**CurrentState** | **string** | Current state of the work item, e.g. Active, Resolved, Closed, etc. | [optional] 
**Description** | **string** | Long description for the work item. | [optional] 
**Id** | **string** | Unique identifier for the work item | [optional] 
**ProviderName** | **string** | The name of the provider the work item is associated with. | [optional] 
**Title** | **string** | Short name for the work item. | [optional] 
**Type** | **string** | Type of work item, e.g. Bug, Task, User Story, etc. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

