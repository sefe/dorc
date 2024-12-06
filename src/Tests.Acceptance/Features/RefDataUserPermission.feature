#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataUserPermission


#TestCaseReference(3012)
Scenario: 001 Gets list of Permissions
	Given I have created GET request on RefDataPermission
	Then The result should be list of permissions

#TestCaseReference(3013)
Scenario: 02 Get user permissions for database
	Given I have created GET request on RefDataUserPermissions with '426' as 'userId' and '1193' as 'databaseId' and '307' as 'envId'
	Then The result should be list with '1' permission which name is 'olf_user'

#TestCaseReference(3014)
Scenario: 03 Add database permission
	Given I have created PUT request to RefDataUserPermissions '426' as 'userId' and '1193' as 'dbId' and '35009' as 'permissionId' and '307' as 'envId'
	Then The result should be 'true'

#TestCaseReference(3015)
Scenario: 04 Remove database permission
	Given I have created DELETE request to RefDataUserPermissions '426' as 'userId' and '1193' as 'dbId' and '35009' as 'permissionId' and '307' as 'envId'
	Then The result should be 'true' in request
