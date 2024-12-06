#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataEnvironmentsUsers

#TestCaseReference(3003)
Scenario: Get Endur users for environment id
	Given I have created GET request to RefDataEnvironmentsUsers with query parameters 'id'='307' and 'type'='endur'
	Then The result should be list of endur users with amount '54'

#TestCaseReference(3004)
Scenario: Get users for Environment
	Given The number of users for the environment with id '307' is known
	And I have created GET request to RefDataEnvironmentsUsers with query 'id'='307'
	Then The result should be list of environment users with known amount
