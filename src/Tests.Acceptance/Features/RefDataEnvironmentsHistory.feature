#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataEnvironmentsHistory


#TestCaseReference(2999)
Scenario: Returns Environment history
	Given I have created RefDataEnvironments with 'id'='307'
	Then The result should contain list of history items with environment name 'Endur DV 01'
