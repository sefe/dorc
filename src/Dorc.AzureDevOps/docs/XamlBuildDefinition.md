# Org.OpenAPITools.Model.XamlBuildDefinition

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**Links** | [**ReferenceLinks**](ReferenceLinks.md) |  | [optional] 
**BatchSize** | **int** | Batch size of the definition | [optional] 
**BuildArgs** | **string** |  | [optional] 
**ContinuousIntegrationQuietPeriod** | **int** | The continuous integration quiet period | [optional] 
**Controller** | [**BuildController**](BuildController.md) |  | [optional] 
**CreatedOn** | **DateTime** | The date this definition was created | [optional] 
**DefaultDropLocation** | **string** | Default drop location for builds from this definition | [optional] 
**Description** | **string** | Description of the definition | [optional] 
**LastBuild** | [**XamlBuildReference**](XamlBuildReference.md) |  | [optional] 
**Repository** | [**BuildRepository**](BuildRepository.md) |  | [optional] 
**SupportedReasons** | **string** | The reasons supported by the template | [optional] 
**TriggerType** | **string** | How builds are triggered from this definition | [optional] 
**CreatedDate** | **DateTime** | The date this version of the definition was created. | [optional] 
**Id** | **int** | The ID of the referenced definition. | [optional] 
**Name** | **string** | The name of the referenced definition. | [optional] 
**Path** | **string** | The folder path of the definition. | [optional] 
**Project** | [**TeamProjectReference**](TeamProjectReference.md) |  | [optional] 
**QueueStatus** | **string** | A value that indicates whether builds can be queued against this definition. | [optional] 
**Revision** | **int** | The definition revision number. | [optional] 
**Type** | **string** | The type of the definition. | [optional] 
**Uri** | **string** | The definition&#39;s URI. | [optional] 
**Url** | **string** | The REST URL of the definition. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

