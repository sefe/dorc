-- DOrc Demo Playground - Seed Data
-- This script populates the demo environment with sample data
-- Run after initial dacpac deployment

USE [dorc-demo];
GO

SET NOCOUNT ON;

-- ============================================================================
-- Secure Key (required for property encryption)
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM [deploy].[SecureKey])
BEGIN
    INSERT INTO [deploy].[SecureKey] ([IV], [Key])
    VALUES ('DEMO_IV_CHANGE_ME_1234567890123456', 'DEMO_KEY_CHANGE_ME_12345678901234567890123456789012345678901234567890');
END
GO

-- ============================================================================
-- Environments
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM [deploy].[Environment] WHERE [Name] = 'DEV')
BEGIN
    INSERT INTO [deploy].[Environment] ([Name], [Secure], [IsProd], [Owner], [Description])
    VALUES
        ('DEV',     0, 0, 'demo-admin', 'Development environment'),
        ('QA',      0, 0, 'demo-admin', 'Quality Assurance environment'),
        ('STAGING', 1, 0, 'demo-admin', 'Pre-production staging environment'),
        ('PROD',    1, 1, 'demo-admin', 'Production environment');
END
GO

-- ============================================================================
-- Projects
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM [deploy].[Project] WHERE [Name] = 'WebApp')
BEGIN
    INSERT INTO [deploy].[Project] ([ObjectId], [Name], [Description], [ArtefactsUrl])
    VALUES
        (NEWID(), 'WebApp',           'Sample web application',               'https://dev.azure.com/demo/WebApp'),
        (NEWID(), 'API-Gateway',      'API Gateway microservice',             'https://dev.azure.com/demo/APIGateway'),
        (NEWID(), 'DataPipeline',     'ETL data processing pipeline',         'https://dev.azure.com/demo/DataPipeline'),
        (NEWID(), 'InfraAsCode',      'Infrastructure as Code (Terraform)',   'https://dev.azure.com/demo/InfraAsCode');
END
GO

-- ============================================================================
-- Scripts
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM [deploy].[Script] WHERE [Name] = 'Deploy-WebApp')
BEGIN
    INSERT INTO [deploy].[Script] ([Name], [Path], [IsPathJSON], [NonProdOnly], [PowerShellVersionNumber])
    VALUES
        ('Deploy-WebApp',       'scripts\Deploy-WebApp.ps1',        0, 0, '7'),
        ('Deploy-APIGateway',   'scripts\Deploy-APIGateway.ps1',    0, 0, '7'),
        ('Run-DBMigrations',    'scripts\Run-DBMigrations.ps1',     0, 0, '7'),
        ('Run-SmokeTests',      'scripts\Run-SmokeTests.ps1',       0, 1, '7'),
        ('Deploy-Config',       'scripts\Deploy-Config.ps1',        0, 0, '7');
END
GO

-- ============================================================================
-- Components
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM [deploy].[Component] WHERE [Name] = 'WebApp-Frontend')
BEGIN
    DECLARE @scriptDeployWebApp INT = (SELECT Id FROM [deploy].[Script] WHERE [Name] = 'Deploy-WebApp');
    DECLARE @scriptDeployAPI INT = (SELECT Id FROM [deploy].[Script] WHERE [Name] = 'Deploy-APIGateway');
    DECLARE @scriptDBMigrations INT = (SELECT Id FROM [deploy].[Script] WHERE [Name] = 'Run-DBMigrations');
    DECLARE @scriptSmokeTests INT = (SELECT Id FROM [deploy].[Script] WHERE [Name] = 'Run-SmokeTests');
    DECLARE @scriptDeployConfig INT = (SELECT Id FROM [deploy].[Script] WHERE [Name] = 'Deploy-Config');

    INSERT INTO [deploy].[Component] ([Name], [Description], [IsEnabled], [StopOnFailure], [ScriptId], [ComponentType])
    VALUES
        ('WebApp-Frontend',     'Frontend web application deployment',      1, 1, @scriptDeployWebApp,    0),
        ('WebApp-Backend',      'Backend API deployment',                   1, 1, @scriptDeployAPI,       0),
        ('Database-Migration',  'Database schema migrations',               1, 1, @scriptDBMigrations,    0),
        ('Smoke-Tests',         'Post-deployment smoke tests',              1, 0, @scriptSmokeTests,      0),
        ('Config-Deploy',       'Configuration file deployment',            1, 1, @scriptDeployConfig,    0),
        ('Infra-Network',       'Network infrastructure (Terraform)',       1, 1, NULL,                   1);
END
GO

-- ============================================================================
-- Project-Component Mappings
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM [deploy].[ProjectComponent])
BEGIN
    DECLARE @projWebApp INT = (SELECT Id FROM [deploy].[Project] WHERE [Name] = 'WebApp');
    DECLARE @projAPIGW INT = (SELECT Id FROM [deploy].[Project] WHERE [Name] = 'API-Gateway');
    DECLARE @projData INT = (SELECT Id FROM [deploy].[Project] WHERE [Name] = 'DataPipeline');
    DECLARE @projInfra INT = (SELECT Id FROM [deploy].[Project] WHERE [Name] = 'InfraAsCode');

    DECLARE @compFrontend INT = (SELECT Id FROM [deploy].[Component] WHERE [Name] = 'WebApp-Frontend');
    DECLARE @compBackend INT = (SELECT Id FROM [deploy].[Component] WHERE [Name] = 'WebApp-Backend');
    DECLARE @compDB INT = (SELECT Id FROM [deploy].[Component] WHERE [Name] = 'Database-Migration');
    DECLARE @compSmoke INT = (SELECT Id FROM [deploy].[Component] WHERE [Name] = 'Smoke-Tests');
    DECLARE @compConfig INT = (SELECT Id FROM [deploy].[Component] WHERE [Name] = 'Config-Deploy');
    DECLARE @compInfra INT = (SELECT Id FROM [deploy].[Component] WHERE [Name] = 'Infra-Network');

    INSERT INTO [deploy].[ProjectComponent] ([ProjectId], [ComponentId])
    VALUES
        (@projWebApp, @compFrontend),
        (@projWebApp, @compBackend),
        (@projWebApp, @compDB),
        (@projWebApp, @compSmoke),
        (@projAPIGW,  @compBackend),
        (@projAPIGW,  @compConfig),
        (@projData,   @compDB),
        (@projData,   @compConfig),
        (@projInfra,  @compInfra);
END
GO

-- ============================================================================
-- Project-Environment Mappings
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM [deploy].[ProjectEnvironment])
BEGIN
    DECLARE @projWebApp2 INT = (SELECT Id FROM [deploy].[Project] WHERE [Name] = 'WebApp');
    DECLARE @projAPIGW2 INT = (SELECT Id FROM [deploy].[Project] WHERE [Name] = 'API-Gateway');
    DECLARE @projData2 INT = (SELECT Id FROM [deploy].[Project] WHERE [Name] = 'DataPipeline');
    DECLARE @projInfra2 INT = (SELECT Id FROM [deploy].[Project] WHERE [Name] = 'InfraAsCode');

    DECLARE @envDev INT = (SELECT Id FROM [deploy].[Environment] WHERE [Name] = 'DEV');
    DECLARE @envQA INT = (SELECT Id FROM [deploy].[Environment] WHERE [Name] = 'QA');
    DECLARE @envStaging INT = (SELECT Id FROM [deploy].[Environment] WHERE [Name] = 'STAGING');
    DECLARE @envProd INT = (SELECT Id FROM [deploy].[Environment] WHERE [Name] = 'PROD');

    INSERT INTO [deploy].[ProjectEnvironment] ([ProjectId], [EnvironmentId])
    VALUES
        (@projWebApp2, @envDev),
        (@projWebApp2, @envQA),
        (@projWebApp2, @envStaging),
        (@projWebApp2, @envProd),
        (@projAPIGW2,  @envDev),
        (@projAPIGW2,  @envQA),
        (@projAPIGW2,  @envStaging),
        (@projAPIGW2,  @envProd),
        (@projData2,   @envDev),
        (@projData2,   @envQA),
        (@projInfra2,  @envDev),
        (@projInfra2,  @envQA),
        (@projInfra2,  @envStaging),
        (@projInfra2,  @envProd);
END
GO

-- ============================================================================
-- Properties (deployment variables)
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM [deploy].[Property] WHERE [Name] = 'ServerName')
BEGIN
    INSERT INTO [deploy].[Property] ([Name], [Description], [Secure], [IsArray])
    VALUES
        ('ServerName',          'Target server hostname',                    0, 0),
        ('DatabaseServer',      'Database server connection string',         0, 0),
        ('DatabaseName',        'Target database name',                      0, 0),
        ('AppPoolName',         'IIS Application Pool name',                 0, 0),
        ('SiteName',            'IIS Site name',                             0, 0),
        ('ConnectionString',    'Application connection string',             1, 0),
        ('ApiKey',              'External API key',                          1, 0),
        ('EnvironmentName',     'Environment identifier',                    0, 0),
        ('DeployPath',          'Deployment target path',                    0, 0),
        ('LogLevel',            'Application log level',                     0, 0);
END
GO

-- ============================================================================
-- Config Values (DOrc system config)
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM [deploy].[ConfigValue] WHERE [Name] = 'DORC_NonProdDeployUsername')
BEGIN
    INSERT INTO [deploy].[ConfigValue] ([Name], [Value])
    VALUES
        ('DORC_NonProdDeployUsername', 'demo-deploy-user'),
        ('DORC_NonProdDeployPassword', 'demo-deploy-password'),
        ('DORC_ProdDeployUsername', 'demo-deploy-user'),
        ('DORC_ProdDeployPassword', 'demo-deploy-password');
END
GO

PRINT 'Demo seed data applied successfully.';
GO
