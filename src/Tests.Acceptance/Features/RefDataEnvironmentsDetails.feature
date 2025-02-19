#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataEnvironmentsDetails
	
@ignore #The endpoint requested in this scenario exists only in the previous version of DOrc.
@Environments @Projects
#TestCaseReference(3000)
Scenario: Returns environments for project
	Given I have created GET request to RefDataEnvironmentsDetails with query 'project'='Endur'
	Then The result should be json with project equals 'Endur' and list of environments whose names should contain 'Endur'

@Environments
#TestCaseReference(3001)
Scenario Outline: Return detailed information about environment items: db's, apps and etc
	Given I have Created GET request to RefDataEnvironmentsDetails with query 'id'='<id>'
	Then The result should be json with environment '<name>' and contain '<databases>' and '<servers>'
	Examples: 
	| id    | name               | databases | servers |
	| 280   | FO Apps DV 01      | 20        | 2       |
	| 307   | Endur DV 01        | 7         | 2       |

@Environments @Components
#TestCaseReference(3002)
Scenario Outline: Returns Environment components
	Given I have created Get request on RefDataEnvironmentsDetails with query 'id'='<id>' and 'type'='<type>'
	Then the result should be list of '<type>' with '<count>' elements
	Examples: 
	| id | type | count |
	| 280  | 0    | 20    |
	| 280  | 1    | 2     |

@Environments @Components
#TestCaseReference(2997)
Scenario Outline: Add or remove environment components 
	Given I have created PUT request vith query 'envId'='<envId>' 'componentId'='<componentId>' 'action'='<action>' 'component'='<component>'
	Then The result should be ApiBoolResult with Result 'true'
	Examples: 
	| envId | componentId | action | component |
	| 280     | 1           | detach | database  |
	| 280     | 1           | attach | database  |
	| 280     | 1           | detach | server    |
	| 280     | 1           | attach | server    |

@Environments
Scenario: Set parent for environment
	Given Environment with name 'Parent1 Env Testing' created
	And Environment with name 'Child1 Env Testing' created
	When I set parent to 'Child1 Env Testing' environment equals the ID of the environment with name 'Parent1 Env Testing'
	Then The result should be Environment 'Child1 Env Testing' with Parent

@Environments
Scenario: Set parent for child environment
	Given Environment with name 'Parent2 Env Testing' created
	And Environment with name 'Child2 Env Testing' created
	And Environment with name 'Child3 Env Testing' created
	When I set parent to 'Child2 Env Testing' environment equals the ID of the environment with name 'Parent2 Env Testing'
	And I set parent to 'Child3 Env Testing' environment equals the ID of the environment with name 'Child2 Env Testing'
	Then The result should not be Environment 'Child3 Env Testing' with Parent