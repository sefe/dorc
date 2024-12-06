#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataUsers
	Api testing



#TestCaseReference(3016)
Scenario: Get all accounts list	
	Given I have created GET request on RefDataUsers endpoint
	Then the result should be list of users and groups

#TestCaseReference(3017)
Scenario: Get users list
	Given I have created GET request on RefDataUsers endpoint with '1' as type
	Then the result should be list of users only

#TestCaseReference(3018)
Scenario: Get Groups list
	Given I have created GET request on RefDataUsers endpoint with '2' as type
	Then The result should be list of groups only
@adduser
#TestCaseReference(3019)
Scenario: Create new user
	Given I have created POST request on RefDataUsers endpoint with following data
	"""
	{"DisplayName":"autoDisplayName","LoginType":"Endur","LanIdType":"User","LanId":"autoLanId","LoginId":"autoLanId","LoginId":"autoLanId","Team":"autoTeam"}
	"""
	Then the result should be new user

#TestCaseReference(3020)
Scenario Outline: Get user by lan ID
	Given I have created GET request on RefDataUsers endpoint with following '<value>'
	Then the result should be the user with LanId equals '<result>'
	Examples: 
		| value           | result    |
		| autoLanId       | autoLanId |
		| autoDisplayName | autoLanId |
