# Org.OpenAPITools.Model.PropertiesCollection
The class represents a property bag as a collection of key-value pairs. Values of all primitive types (any type with a `TypeCode != TypeCode.Object`) except for `DBNull` are accepted. Values of type Byte[], Int32, Double, DateType and String preserve their type, other primitives are retuned as a String. Byte[] expected as base64 encoded string.

## Properties

Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**Count** | **int** | The count of properties in the collection. | [optional] 
**Item** | **Object** |  | [optional] 
**Keys** | **List&lt;string&gt;** | The set of keys in the collection. | [optional] 
**Values** | **List&lt;string&gt;** | The set of values in the collection. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)

