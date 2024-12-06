# Org.OpenAPITools.Model.AgentPoolQueueTarget
Describes how a phase should run against an agent queue.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**AgentSpecification** | [**AgentSpecification**](AgentSpecification.md) |  | [optional] 
**AllowScriptsAuthAccessOption** | **bool** | Enables scripts and other processes launched while executing phase to access the OAuth token | [optional] 
**Demands** | [**List&lt;Demand&gt;**](Demand.md) |  | [optional] 
**ExecutionOptions** | [**AgentTargetExecutionOptions**](AgentTargetExecutionOptions.md) |  | [optional] 
**Queue** | [**AgentPoolQueue**](AgentPoolQueue.md) |  | [optional] 
**Type** | **int** | The type of the target. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

