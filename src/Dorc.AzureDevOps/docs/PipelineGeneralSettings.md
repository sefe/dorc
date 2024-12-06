# Org.OpenAPITools.Model.PipelineGeneralSettings
Contains pipeline general settings.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**EnforceJobAuthScope** | **bool** | If enabled, scope of access for all pipelines reduces to the current project. | [optional] 
**EnforceReferencedRepoScopedToken** | **bool** | Restricts the scope of access for all pipelines to only repositories explicitly referenced by the pipeline. | [optional] 
**EnforceSettableVar** | **bool** | If enabled, only those variables that are explicitly marked as \&quot;Settable at queue time\&quot; can be set at queue time. | [optional] 
**PublishPipelineMetadata** | **bool** | Allows pipelines to record metadata. | [optional] 
**StatusBadgesArePrivate** | **bool** | Anonymous users can access the status badge API for all pipelines unless this option is enabled. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

