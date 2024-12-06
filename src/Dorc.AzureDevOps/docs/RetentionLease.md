# Org.OpenAPITools.Model.RetentionLease
A valid retention lease prevents automated systems from deleting a pipeline run.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**CreatedOn** | **DateTime** | When the lease was created. | [optional] 
**DefinitionId** | **int** | The pipeline definition of the run. | [optional] 
**LeaseId** | **int** | The unique identifier for this lease. | [optional] 
**OwnerId** | **string** | Non-unique string that identifies the owner of a retention lease. | [optional] 
**RunId** | **int** | The pipeline run protected by this lease. | [optional] 
**ValidUntil** | **DateTime** | The last day the lease is considered valid. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

