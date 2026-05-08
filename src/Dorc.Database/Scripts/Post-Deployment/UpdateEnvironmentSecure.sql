/*
Post-Deployment Script: Fixes bug that appeared after the behaviour of IsProd and IsSecure
Checkbox changes Idempotent: only inserts rows that don't already exist.
*/

UPDATE [deploy].[Environment]
SET [Secure] = 1
WHERE [IsProd] = 1 AND [Secure] != 1;