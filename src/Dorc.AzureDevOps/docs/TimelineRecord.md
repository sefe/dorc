# Org.OpenAPITools.Model.TimelineRecord
Represents an entry in a build's timeline.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**Links** | [**ReferenceLinks**](ReferenceLinks.md) |  | [optional] 
**Attempt** | **int** | Attempt number of record. | [optional] 
**ChangeId** | **int** | The change ID. | [optional] 
**CurrentOperation** | **string** | A string that indicates the current operation. | [optional] 
**Details** | [**TimelineReference**](TimelineReference.md) |  | [optional] 
**ErrorCount** | **int** | The number of errors produced by this operation. | [optional] 
**FinishTime** | **DateTime** | The finish time. | [optional] 
**Id** | **Guid** | The ID of the record. | [optional] 
**Identifier** | **string** | String identifier that is consistent across attempts. | [optional] 
**Issues** | [**List&lt;Issue&gt;**](Issue.md) |  | [optional] 
**LastModified** | **DateTime** | The time the record was last modified. | [optional] 
**Log** | [**BuildLogReference**](BuildLogReference.md) |  | [optional] 
**Name** | **string** | The name. | [optional] 
**Order** | **int** | An ordinal value relative to other records. | [optional] 
**ParentId** | **Guid** | The ID of the record&#39;s parent. | [optional] 
**PercentComplete** | **int** | The current completion percentage. | [optional] 
**PreviousAttempts** | [**List&lt;TimelineAttempt&gt;**](TimelineAttempt.md) |  | [optional] 
**QueueId** | **int** | The queue ID of the queue that the operation ran on. | [optional] 
**Result** | **string** | The result. | [optional] 
**ResultCode** | **string** | The result code. | [optional] 
**StartTime** | **DateTime** | The start time. | [optional] 
**State** | **string** | The state of the record. | [optional] 
**Task** | [**TaskReference**](TaskReference.md) |  | [optional] 
**Type** | **string** | The type of the record. | [optional] 
**Url** | **string** | The REST URL of the timeline record. | [optional] 
**WarningCount** | **int** | The number of warnings produced by this operation. | [optional] 
**WorkerName** | **string** | The name of the agent running the operation. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

