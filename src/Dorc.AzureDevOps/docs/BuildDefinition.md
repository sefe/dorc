# Org.OpenAPITools.Model.BuildDefinition
Represents a build definition.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**BadgeEnabled** | **bool** | Indicates whether badges are enabled for this definition. | [optional] 
**BuildNumberFormat** | **string** | The build number format. | [optional] 
**Comment** | **string** | A save-time comment for the definition. | [optional] 
**Demands** | [**List&lt;Demand&gt;**](Demand.md) |  | [optional] 
**Description** | **string** | The description. | [optional] 
**DropLocation** | **string** | The drop location for the definition. | [optional] 
**JobAuthorizationScope** | **string** | The job authorization scope for builds queued against this definition. | [optional] 
**JobCancelTimeoutInMinutes** | **int** | The job cancel timeout (in minutes) for builds cancelled by user for this definition. | [optional] 
**JobTimeoutInMinutes** | **int** | The job execution timeout (in minutes) for builds queued against this definition. | [optional] 
**Options** | [**List&lt;BuildOption&gt;**](BuildOption.md) |  | [optional] 
**Process** | [**BuildProcess**](BuildProcess.md) |  | [optional] 
**ProcessParameters** | [**ProcessParameters**](ProcessParameters.md) |  | [optional] 
**Properties** | [**PropertiesCollection**](PropertiesCollection.md) |  | [optional] 
**Repository** | [**BuildRepository**](BuildRepository.md) |  | [optional] 
**RetentionRules** | [**List&lt;RetentionPolicy&gt;**](RetentionPolicy.md) |  | [optional] 
**Tags** | **List&lt;string&gt;** |  | [optional] 
**Triggers** | [**List&lt;BuildTrigger&gt;**](BuildTrigger.md) |  | [optional] 
**VariableGroups** | [**List&lt;VariableGroup&gt;**](VariableGroup.md) |  | [optional] 
**Variables** | [**Dictionary&lt;string, BuildDefinitionVariable&gt;**](BuildDefinitionVariable.md) |  | [optional] 
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

