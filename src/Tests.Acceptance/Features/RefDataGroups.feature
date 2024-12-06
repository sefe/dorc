#TestPlanReference(2981)
#TestSuiteReference(2983)
Feature: RefDataGroups


#TestCaseReference(3005)
Scenario: Returns list of AD groups
	Given I have created GET request to RefDataGroupsFeature
	Then The result should be list of groups

#TestCaseReference(3006)
Scenario: Returns AD Group by name
	Given I have created GET request to RefDataGroupsFeature with query parameter 'name' and value 'Citrix Endur Sandbox Access'
	Then The result should be group with id '1' and name 'Citrix Endur Sandbox Access'
