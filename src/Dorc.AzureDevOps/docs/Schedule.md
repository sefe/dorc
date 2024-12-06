# Org.OpenAPITools.Model.Schedule

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**BranchFilters** | **List&lt;string&gt;** |  | [optional] 
**DaysToBuild** | **string** | Days for a build (flags enum for days of the week) | [optional] 
**ScheduleJobId** | **Guid** | The Job Id of the Scheduled job that will queue the scheduled build. Since a single trigger can have multiple schedules and we want a single job to process a single schedule (since each schedule has a list of branches to build), the schedule itself needs to define the Job Id. This value will be filled in when a definition is added or updated.  The UI does not provide it or use it. | [optional] 
**ScheduleOnlyWithChanges** | **bool** | Flag to determine if this schedule should only build if the associated source has been changed. | [optional] 
**StartHours** | **int** | Local timezone hour to start | [optional] 
**StartMinutes** | **int** | Local timezone minute to start | [optional] 
**TimeZoneId** | **string** | Time zone of the build schedule (String representation of the time zone ID) | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

