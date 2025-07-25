-- Migration script to preserve environment history audit records when environments are deleted
-- This modifies the foreign key constraint to allow EnvId to be set to NULL when environment is deleted

-- First, make the EnvId column nullable to allow orphaned audit records
ALTER TABLE [deploy].[EnvironmentHistory] 
ALTER COLUMN [EnvId] INT NULL;

-- Drop the existing foreign key constraint
ALTER TABLE [deploy].[EnvironmentHistory]
DROP CONSTRAINT [EnvironmentHistory_Environment_Env_ID_fk];

-- Recreate the foreign key constraint with SET NULL on delete
-- This will preserve audit records by setting EnvId to NULL when environment is deleted
ALTER TABLE [deploy].[EnvironmentHistory]
ADD CONSTRAINT [EnvironmentHistory_Environment_Env_ID_fk] 
FOREIGN KEY ([EnvId]) 
REFERENCES [deploy].[Environment] ([Id])
ON DELETE SET NULL;