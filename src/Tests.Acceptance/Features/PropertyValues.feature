#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: PropertyValues


#TestCaseReference(2987)
Scenario: Get properties list 
	Given I have created GET request to PropertyValues with query 'environmentName'='Endur DV 10'
	Then The result should be non empty list of properties and not contains dorc system properties
