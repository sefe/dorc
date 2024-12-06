# Org.OpenAPITools.Model.Phase
Represents a phase of a build definition.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**Condition** | **string** | The condition that must be true for this phase to execute. | [optional] 
**Dependencies** | [**List&lt;Dependency&gt;**](Dependency.md) |  | [optional] 
**JobAuthorizationScope** | **string** | The job authorization scope for builds queued against this definition. | [optional] 
**JobCancelTimeoutInMinutes** | **int** | The cancellation timeout, in minutes, for builds queued against this definition. | [optional] 
**JobTimeoutInMinutes** | **int** | The job execution timeout, in minutes, for builds queued against this definition. | [optional] 
**Name** | **string** | The name of the phase. | [optional] 
**RefName** | **string** | The unique ref name of the phase. | [optional] 
**Steps** | [**List&lt;BuildDefinitionStep&gt;**](BuildDefinitionStep.md) |  | [optional] 
**Target** | [**PhaseTarget**](PhaseTarget.md) |  | [optional] 
**Variables** | [**Dictionary&lt;string, BuildDefinitionVariable&gt;**](BuildDefinitionVariable.md) |  | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

