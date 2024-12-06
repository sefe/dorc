#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataServices
	ApiTests

@ignore
#TestCaseReference(3011)
Scenario Outline:  Change service state. Returns new service status
	Given I have created PUT request to RefDataServices with body contains json with '<ServerName>' '<ServiceName>' '<ServiceStatus>'
	Then The '<result>' should be equal ServiceStatusApiModel ServiceStatus
	Examples: 
		| ServerName | ServiceName              | ServiceStatus | result |
		| DEPAPP02DV |DeploymentActionServiceNonProd| stop          |stopped|
		| DEPAPP02DV |DeploymentActionServiceNonProd| start         |started|
	
