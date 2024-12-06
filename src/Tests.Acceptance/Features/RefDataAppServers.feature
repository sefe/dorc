#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataAppServers


#TestCaseReference(2988)
Scenario: Get app servers list 
	Given I have created GET request to RefDataAppServers with query 'id'='80'
	Then The result should be non empty list of appservers
