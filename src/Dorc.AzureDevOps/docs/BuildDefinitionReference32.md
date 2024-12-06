# Org.OpenAPITools.Model.BuildDefinitionReference32
For back-compat with extensions that use the old Steps format instead of Process and Phases

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**Links** | [**ReferenceLinks**](ReferenceLinks.md) |  | [optional] 
**AuthoredBy** | [**IdentityRef**](IdentityRef.md) |  | [optional] 
**DraftOf** | [**DefinitionReference**](DefinitionReference.md) |  | [optional] 
**Drafts** | [**List&lt;DefinitionReference&gt;**](DefinitionReference.md) | The list of drafts associated with this definition, if this is not a draft definition. | [optional] 
**Metrics** | [**List&lt;BuildMetric&gt;**](BuildMetric.md) |  | [optional] 
**Quality** | **string** | The quality of the definition document (draft, etc.) | [optional] 
**Queue** | [**AgentPoolQueue**](AgentPoolQueue.md) |  | [optional] 
**CreatedDate** | **DateTime** | The date this version of the definition was created. | [optional] 
**Id** | **int** | The ID of the referenced definition. | [optional] 
**Name** | **string** | The name of the referenced definition. | [optional] 
**Path** | **string** | The folder path of the definition. | [optional] 
**Project** | [**TeamProjectReference**](TeamProjectReference.md) |  | [optional] 
**QueueStatus** | **string** | A value that indicates whether builds can be queued against this definition. | [optional] 
**Revision** | **int** | The definition revision number. | [optional] 
**Type** | **string** | The type of the definition. | [optional] 
**Uri** | **string** | The definition&#39;s URI. | [optional] 
**Url** | **string** | The REST URL of the definition. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

