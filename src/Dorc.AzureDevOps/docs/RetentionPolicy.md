# Org.OpenAPITools.Model.RetentionPolicy
Represents a retention policy for a build definition.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**Artifacts** | **List&lt;string&gt;** |  | [optional] 
**ArtifactTypesToDelete** | **List&lt;string&gt;** |  | [optional] 
**Branches** | **List&lt;string&gt;** |  | [optional] 
**DaysToKeep** | **int** | The number of days to keep builds. | [optional] 
**DeleteBuildRecord** | **bool** | Indicates whether the build record itself should be deleted. | [optional] 
**DeleteTestResults** | **bool** | Indicates whether to delete test results associated with the build. | [optional] 
**MinimumToKeep** | **int** | The minimum number of builds to keep. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

