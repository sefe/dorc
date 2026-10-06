#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataEnvironmentsUsers

#TestCaseReference(3003)
Scenario: Get Endur users for environment id
	Given I have created GET request to RefDataEnvironmentsUsers with query parameters 'id'='307' and 'type'='endur'
	Then The result should be list of endur users with amount '54'
