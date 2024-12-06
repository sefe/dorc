# Org.OpenAPITools.Model.BuildDefinitionStep
Represents a step in a build phase.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**AlwaysRun** | **bool** | Indicates whether this step should run even if a previous step fails. | [optional] 
**Condition** | **string** | A condition that determines whether this step should run. | [optional] 
**ContinueOnError** | **bool** | Indicates whether the phase should continue even if this step fails. | [optional] 
**DisplayName** | **string** | The display name for this step. | [optional] 
**Enabled** | **bool** | Indicates whether the step is enabled. | [optional] 
**Environment** | **Dictionary&lt;string, string&gt;** |  | [optional] 
**Inputs** | **Dictionary&lt;string, string&gt;** |  | [optional] 
**RefName** | **string** | The reference name for this step. | [optional] 
**Task** | [**TaskDefinitionReference**](TaskDefinitionReference.md) |  | [optional] 
**TimeoutInMinutes** | **int** | The time, in minutes, that this step is allowed to run. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

