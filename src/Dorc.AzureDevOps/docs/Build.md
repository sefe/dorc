# Org.OpenAPITools.Model.Build
Data representation of a build.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**Links** | [**ReferenceLinks**](ReferenceLinks.md) |  | [optional] 
**AgentSpecification** | [**AgentSpecification**](AgentSpecification.md) |  | [optional] 
**BuildNumber** | **string** | The build number/name of the build. | [optional] 
**BuildNumberRevision** | **int** | The build number revision. | [optional] 
**Controller** | [**BuildController**](BuildController.md) |  | [optional] 
**Definition** | [**DefinitionReference**](DefinitionReference.md) |  | [optional] 
**Deleted** | **bool** | Indicates whether the build has been deleted. | [optional] 
**DeletedBy** | [**IdentityRef**](IdentityRef.md) |  | [optional] 
**DeletedDate** | **DateTime** | The date the build was deleted. | [optional] 
**DeletedReason** | **string** | The description of how the build was deleted. | [optional] 
**Demands** | [**List&lt;Demand&gt;**](Demand.md) | A list of demands that represents the agent capabilities required by this build. | [optional] 
**FinishTime** | **DateTime** | The time that the build was completed. | [optional] 
**Id** | **int** | The ID of the build. | [optional] 
**KeepForever** | **bool** | Indicates whether the build should be skipped by retention policies. | [optional] 
**LastChangedBy** | [**IdentityRef**](IdentityRef.md) |  | [optional] 
**LastChangedDate** | **DateTime** | The date the build was last changed. | [optional] 
**Logs** | [**BuildLogReference**](BuildLogReference.md) |  | [optional] 
**OrchestrationPlan** | [**TaskOrchestrationPlanReference**](TaskOrchestrationPlanReference.md) |  | [optional] 
**Parameters** | **string** | The parameters for the build. | [optional] 
**Plans** | [**List&lt;TaskOrchestrationPlanReference&gt;**](TaskOrchestrationPlanReference.md) | Orchestration plans associated with the build (build, cleanup) | [optional] 
**Priority** | **string** | The build&#39;s priority. | [optional] 
**Project** | [**TeamProjectReference**](TeamProjectReference.md) |  | [optional] 
**Properties** | [**PropertiesCollection**](PropertiesCollection.md) |  | [optional] 
**Quality** | **string** | The quality of the xaml build (good, bad, etc.) | [optional] 
**Queue** | [**AgentPoolQueue**](AgentPoolQueue.md) |  | [optional] 
**QueueOptions** | **string** | Additional options for queueing the build. | [optional] 
**QueuePosition** | **int** | The current position of the build in the queue. | [optional] 
**QueueTime** | **DateTime** | The time that the build was queued. | [optional] 
**Reason** | **string** | The reason that the build was created. | [optional] 
**Repository** | [**BuildRepository**](BuildRepository.md) |  | [optional] 
**RequestedBy** | [**IdentityRef**](IdentityRef.md) |  | [optional] 
**RequestedFor** | [**IdentityRef**](IdentityRef.md) |  | [optional] 
**Result** | **string** | The build result. | [optional] 
**RetainedByRelease** | **bool** | Indicates whether the build is retained by a release. | [optional] 
**SourceBranch** | **string** | The source branch. | [optional] 
**SourceVersion** | **string** | The source version. | [optional] 
**StartTime** | **DateTime** | The time that the build was started. | [optional] 
**Status** | **string** | The status of the build. | [optional] 
**Tags** | **List&lt;string&gt;** |  | [optional] 
**TriggeredByBuild** | [**Build**](Build.md) |  | [optional] 
**TriggerInfo** | **Dictionary&lt;string, string&gt;** | Sourceprovider-specific information about what triggered the build | [optional] 
**Uri** | **string** | The URI of the build. | [optional] 
**Url** | **string** | The REST URL of the build. | [optional] 
**ValidationResults** | [**List&lt;BuildRequestValidationResult&gt;**](BuildRequestValidationResult.md) |  | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

