/*
 Pre-Deployment Script Template							
--------------------------------------------------------------------------------------
 This file contains SQL statements that will be executed before the build script.	
 Use SQLCMD syntax to include a file in the pre-deployment script.			
 Example:      :r .\myfile.sql								
 Use SQLCMD syntax to reference a variable in the pre-deployment script.		
 Example:      :setvar TableName MyTable							
               SELECT * FROM [$(TableName)]					
--------------------------------------------------------------------------------------
*/

-- Add EnvironmentOwnerEmail column to DeploymentRequest table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('[deploy].[DeploymentRequest]') AND name = 'EnvironmentOwnerEmail')
BEGIN
    ALTER TABLE [deploy].[DeploymentRequest]
    ADD [EnvironmentOwnerEmail] NVARCHAR(256) NULL
END
