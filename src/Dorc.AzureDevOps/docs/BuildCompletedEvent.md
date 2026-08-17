# Org.OpenAPITools.Model.BuildCompletedEvent

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**BuildId** | **int** |  | [optional] 
**Build** | [**Build**](Build.md) |  | [optional] 
**Changes** | [**List&lt;Change&gt;**](Change.md) | Changes associated with a build used for build notifications | [optional] 
**PullRequest** | [**PullRequest**](PullRequest.md) |  | [optional] 
**TestResults** | [**AggregatedResultsAnalysis**](AggregatedResultsAnalysis.md) |  | [optional] 
**TimelineRecords** | [**List&lt;TimelineRecord&gt;**](TimelineRecord.md) | Timeline records associated with a build used for build notifications | [optional] 
**WorkItems** | [**List&lt;AssociatedWorkItem&gt;**](AssociatedWorkItem.md) | Work items associated with a build used for build notifications | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

