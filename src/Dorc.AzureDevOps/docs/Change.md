# Org.OpenAPITools.Model.Change
Represents a change associated with a build.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**Author** | [**IdentityRef**](IdentityRef.md) |  | [optional] 
**DisplayUri** | **string** | The location of a user-friendly representation of the resource. | [optional] 
**Id** | **string** | The identifier for the change. For a commit, this would be the SHA1. For a TFVC changeset, this would be the changeset ID. | [optional] 
**Location** | **string** | The location of the full representation of the resource. | [optional] 
**Message** | **string** | The description of the change. This might be a commit message or changeset description. | [optional] 
**MessageTruncated** | **bool** | Indicates whether the message was truncated. | [optional] 
**Pusher** | **string** | The person or process that pushed the change. | [optional] 
**Timestamp** | **DateTime** | The timestamp for the change. | [optional] 
**Type** | **string** | The type of change. \&quot;commit\&quot;, \&quot;changeset\&quot;, etc. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

