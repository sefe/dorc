@Variables
Feature: Variables
  As a DOrc user with edit rights on an environment
  I want to change a variable value on the environment's Variables tab
  So that the new value is applied and the change is traceable in the Variables Audit

# Safety net: @Revert:VariableValue restores the original value if the scenario fails part-way
# (see Support/VariableValueRevertHook.cs). Shared environment - always leave it as it was found.

Background:
  Given Launch the application
  And User able to view the Dorc application homepage

@TC374606 @TC374598 @Revert:VariableValue
Scenario: Update an existing environment variable value, verify it in the audit and revert it
  Given the variable "TEVO_AUTONOM_OFFSET_DEFAULT" has an editable value on the Variables tab of environment "TEVO DV 11"
  When the user updates the value of variable "TEVO_AUTONOM_OFFSET_DEFAULT" to "5"
  Then the variable "TEVO_AUTONOM_OFFSET_DEFAULT" shows the value "5" on the Variables tab
  And the latest Variables Audit entry for "TEVO_AUTONOM_OFFSET_DEFAULT" on "TEVO DV 11" shows the original value changed to "5"
  When the user reverts the variable "TEVO_AUTONOM_OFFSET_DEFAULT" to its original value
  Then the variable "TEVO_AUTONOM_OFFSET_DEFAULT" shows its original value on the Variables tab
  And the latest Variables Audit entry for "TEVO_AUTONOM_OFFSET_DEFAULT" on "TEVO DV 11" shows "5" changed back to the original value
