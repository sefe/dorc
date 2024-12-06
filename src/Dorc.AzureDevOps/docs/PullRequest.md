# Org.OpenAPITools.Model.PullRequest
Represents a pull request object.  These are retrieved from Source Providers.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**Links** | [**ReferenceLinks**](ReferenceLinks.md) |  | [optional] 
**Author** | [**IdentityRef**](IdentityRef.md) |  | [optional] 
**CurrentState** | **string** | Current state of the pull request, e.g. open, merged, closed, conflicts, etc. | [optional] 
**Description** | **string** | Description for the pull request. | [optional] 
**Id** | **string** | Unique identifier for the pull request | [optional] 
**ProviderName** | **string** | The name of the provider this pull request is associated with. | [optional] 
**SourceBranchRef** | **string** | Source branch ref of this pull request | [optional] 
**SourceRepositoryOwner** | **string** | Owner of the source repository of this pull request | [optional] 
**TargetBranchRef** | **string** | Target branch ref of this pull request | [optional] 
**TargetRepositoryOwner** | **string** | Owner of the target repository of this pull request | [optional] 
**Title** | **string** | Title of the pull request. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

