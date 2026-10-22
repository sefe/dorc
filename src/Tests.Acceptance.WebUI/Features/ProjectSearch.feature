Feature: Project Search and Navigation

Scenario: Search and view a project in the Environment section
  Given Launch the application
  And User able to view the Dorc application homepage
  When Click the Menu item Environment in the side bar
  And Search the Project "Endur IT 01" in the search bar
  Then "Endur IT 01" project is displayed in the screen
