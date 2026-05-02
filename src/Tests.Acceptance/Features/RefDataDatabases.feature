#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataDatabases


@Databases
#TestCaseReference(2990)
Scenario: Returns Databases list
	Given I have created GET request to RefDataDatabases
	Then The result should be list of databases


#TestCaseReference(2991)
Scenario: Return database details by database ID
	Given I have created GET request to RefDataDatabases with query 'id'='1'
	Then The result should be database with DatabaseName 'END_DB_SANDBOX' and DatabaseId '1'

#TestCaseReference(2992)
Scenario: Return database details by database Name
	Given I have created GET request to RefDataDatabases with query parameter 'name'='SANDBOX' 'server'='SANDBOX'
	Then The result should contain single database with DatabaseName 'SANDBOX' and DatabaseId '941'

#TestCaseReference(2993)
Scenario: Create Database entry
	Given There is no database named 'autoDatabase' on the server named 'autoServerName'
	And I have created POST request to RefDataDatabases with body below
	"""
	{"DatabaseName":"autoDatabase","DatabaseType":"autoDatabaseType","DbServerName":"autoServerName","DbCluster":"autoCluster","AdGroup":"OTHER"}
	"""
	Then The result should be database with id greater than '0' and Name 'autoDatabase'

#TestCaseReference(2994)
Scenario: Attempt to create duplicate Database entry
	Given There is a database named 'duplicateDB' on the server named 'duplicateServer'
	And I have created POST request to RefDataDatabases with body below
	"""
	{"DatabaseName":"duplicateDB","DatabaseType":"autoDatabaseType","DbServerName":"duplicateServer","DbCluster":"autoCluster","AdGroup":"OTHER"}
	"""
	Then The result should be error 'Database already exists duplicateServer:duplicateDB'
