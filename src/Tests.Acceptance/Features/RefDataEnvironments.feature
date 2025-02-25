#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataEnvironments
	
@Environments
#TestCaseReference(2994)
Scenario: Returns list of Environments
	Given I have Created GET request to  RefDataEnvironments
	Then The result should be list of environments

@Environments
#TestCaseReference(2995)
Scenario: Returns environment by name
	Given There is environment with name 'Endur Sandbox Testing'
	And I have created GET request to RefDataEnvironments with query 'env'='Endur Sandbox Testing'
	Then The result should be environment with name 'Endur Sandbox Testing' and id of the existing environment

@Environments
#TestCaseReference(2996)
Scenario: Create new environment
	Given I have created POST request with body
	"""
	{
		"EnvironmentName":"autoEnv",
		"EnvironmentOwner":"Aaron Cross",
		"Description":"autoDescription",
		"RestoredfromSourceDb":"autoBackup",
		"FileShare":"autoShare",
		"ThinClient":"autoServer",
		"Build":"autoBuild",
		"Notes":"autoNotes",
		"Details": {
			"EnvironmentOwner": "Aaron Cross",
			"RestoredFromSourceDb": "autoBackup",
			"Description": "autoDescription",
			"FileShare": "FileShare",
			"ThinClient": "autoServer",
			"Notes": "autoNotes"
		}
	}
	"""
	Then The result should be Environment with id greater than '0'

@Environments
Scenario: Make child environment
	Given There is environment with name 'Parent Env Testing'
	And There is environment with name 'Child Env Testing'
	When I edit the 'Child Env Testing' environment via PUT and set parentId equals to ID of the environment with name 'Parent Env Testing'
	Then The result should be Environment with Parent