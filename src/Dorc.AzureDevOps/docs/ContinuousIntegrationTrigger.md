# Org.OpenAPITools.Model.ContinuousIntegrationTrigger
Represents a continuous integration (CI) trigger.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**BatchChanges** | **bool** | Indicates whether changes should be batched while another CI build is running. | [optional] 
**BranchFilters** | **List&lt;string&gt;** |  | [optional] 
**MaxConcurrentBuildsPerBranch** | **int** | The maximum number of simultaneous CI builds that will run per branch. | [optional] 
**PathFilters** | **List&lt;string&gt;** |  | [optional] 
**PollingInterval** | **int** | The polling interval, in seconds. | [optional] 
**PollingJobId** | **Guid** | The ID of the job used to poll an external repository. | [optional] 
**SettingsSourceType** | **int** |  | [optional] 
**TriggerType** | **string** | The type of the trigger. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

