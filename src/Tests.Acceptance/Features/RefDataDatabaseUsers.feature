#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataDatabaseUsers
	

@Users
#TestCaseReference(2998)
Scenario: Return database user list
	Given I have created GET request to RefDataDatabaseUsers with query 'id'='36848' and 'envId'='280'
	Then The result should be list of '13' users
