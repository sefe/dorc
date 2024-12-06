#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataComponents


@Project
#TestCaseReference(2989)
Scenario: Returns components for Project
	Given I have created GET request to RefDataComponents with query 'id'='DevOps'
	Then the result should contain project with id '29' and non empty list of components 
