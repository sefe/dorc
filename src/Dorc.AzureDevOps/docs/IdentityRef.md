# Org.OpenAPITools.Model.IdentityRef

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**DirectoryAlias** | **string** | Deprecated - Can be retrieved by querying the Graph user referenced in the \&quot;self\&quot; entry of the IdentityRef \&quot;_links\&quot; dictionary | [optional] 
**Id** | **string** |  | [optional] 
**ImageUrl** | **string** | Deprecated - Available in the \&quot;avatar\&quot; entry of the IdentityRef \&quot;_links\&quot; dictionary | [optional] 
**Inactive** | **bool** | Deprecated - Can be retrieved by querying the Graph membership state referenced in the \&quot;membershipState\&quot; entry of the GraphUser \&quot;_links\&quot; dictionary | [optional] 
**IsAadIdentity** | **bool** | Deprecated - Can be inferred from the subject type of the descriptor (Descriptor.IsAadUserType/Descriptor.IsAadGroupType) | [optional] 
**IsContainer** | **bool** | Deprecated - Can be inferred from the subject type of the descriptor (Descriptor.IsGroupType) | [optional] 
**IsDeletedInOrigin** | **bool** |  | [optional] 
**ProfileUrl** | **string** | Deprecated - not in use in most preexisting implementations of ToIdentityRef | [optional] 
**UniqueName** | **string** | Deprecated - use Domain+PrincipalName instead | [optional] 
**Links** | [**ReferenceLinks**](ReferenceLinks.md) |  | [optional] 
**Descriptor** | **string** | The descriptor is the primary way to reference the graph subject while the system is running. This field will uniquely identify the same graph subject across both Accounts and Organizations. | [optional] 
**DisplayName** | **string** | This is the non-unique display name of the graph subject. To change this field, you must alter its value in the source provider. | [optional] 
**Url** | **string** | This url is the full route to the source resource of this graph subject. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

