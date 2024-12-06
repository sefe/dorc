#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataProjectBuilds

#TestCaseReference(3007)
Scenario: Get environment builds
	Given There is at least one record in the EnvironmentComponentStatuses for the environmetn with name 'Endur Sandbox Testing'
	And I have created GET request to RefDataProjectBuilds with 'Endur Sandbox Testing' as id
	Then The result should contain list of EnvBuildsApiModel
