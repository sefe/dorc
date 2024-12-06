#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataServers
	Api tests


#TestCaseReference(3008)
Scenario: Returns list of Servers
	Given I have Created GET request on RefDataServersFeature
	Then the result should be list of ServerApiModel


@addserver
#TestCaseReference(3009)
Scenario: Add new server to Environment tracker database
	Given I have created POST request on RefDataServers with data below in body
	"""
	{
	  "EnvironmentNames": [
		"Endur Sandbox Testing"
	  ],
	  "UserEditable": true,
	  "Name": "autoServerName",
	  "OsName": "2008 R2 Standard",
	  "ApplicationTags": "autoTag1;autoTag2"
	}
	"""
	Then The result should contain  ServerApiModel with ServerID greater than '0'
	

#TestCaseReference(3010)
Scenario: Returns server detail
	Given I have created GET request on RefDataServers with 'autoServerName' as 'server'
	Then the result should be ServerApiModel with 'autoServerName' as ServerName and ServerID must be not equal '0'

