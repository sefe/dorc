export interface GenericScriptParameter {
  name: string;
  label: string;
  placeholder?: string;
  required?: boolean;
  helperText?: string;
  options?: string[];
}

export interface GenericScriptTemplate {
  id: string;
  name: string;
  defaultStepName: string;
  scriptPath: string;
  category:
    | 'Application & MSI'
    | 'SQL & Database'
    | 'Windows Services'
    | 'ActiveBatch'
    | 'Server & Patching'
    | 'SSIS & SSRS'
    | 'Messaging & Certificates';
  description: string;
  parameters: GenericScriptParameter[];
  requiredEnvironmentProperties?: string[];
}

export interface ParsedScriptPath {
  scriptPath: string;
  isJsonPath: boolean;
  arguments: Array<{ key: string; value: string }>;
}

export const GENERIC_SCRIPT_TEMPLATES: GenericScriptTemplate[] = [
  {
    id: 'deploy-msi',
    name: 'Deploy MSI Package',
    defaultStepName: 'Deploy MSI',
    scriptPath: '00 Generic\\DeployMSI.ps1',
    category: 'Application & MSI',
    description:
      'Installs an MSI package from the build drop folder onto target environment servers matching the server tag.',
    parameters: [
      {
        name: 'msiName',
        label: 'MSI file name (msiName)',
        placeholder: 'e.g. MyApplication.Service.msi',
        required: true,
        helperText: 'MSI filename inside the build DropFolder.'
      },
      {
        name: 'productNames',
        label: 'Installed product name (productNames)',
        placeholder: 'e.g. MyApplication Service',
        required: true,
        helperText:
          'Display name in Windows Installed Programs for uninstall/upgrade verification.'
      },
      {
        name: 'ServerTag',
        label: 'Server tag filter (ServerTag)',
        placeholder: 'Optional server role/tag override',
        required: false,
        helperText:
          'Optional tag to target a subset of mapped environment servers.'
      },
      {
        name: 'productNamesEnvironment',
        label: 'Environment suffix property (productNamesEnvironment)',
        placeholder: 'Optional property name',
        required: false,
        helperText:
          'Optional environment property appended to productNames when installed side-by-side.'
      }
    ],
    requiredEnvironmentProperties: [
      'DropFolder',
      'EnvMgtDBServer',
      'EnvMgtDBName',
      'DeploymentServiceAccount',
      'DeploymentServiceAccountPassword'
    ]
  },
  {
    id: 'control-services',
    name: 'Control Windows Services (Start / Stop)',
    defaultStepName: 'Control Windows Services',
    scriptPath: '00 Generic\\ControlServices.ps1',
    category: 'Windows Services',
    description:
      'Starts or stops one or more Windows services across servers matching the specified server types.',
    parameters: [
      {
        name: 'action',
        label: 'Service action (action)',
        placeholder: 'stop or start',
        required: true,
        options: ['stop', 'start'],
        helperText: 'Operation to perform on the target Windows services.'
      },
      {
        name: 'serverTypes',
        label: 'Target server types (serverTypes)',
        placeholder: 'e.g. APP or APP;WEB',
        required: true,
        helperText:
          'Server type tag(s) identifying target hosts in the environment.'
      },
      {
        name: 'services',
        label: 'Service names (services)',
        placeholder: 'e.g. MyAppService1;MyAppService2',
        required: true,
        helperText: 'Windows service name(s), separated by semicolons.'
      }
    ]
  },
  {
    id: 'deploy-dacpac',
    name: 'Deploy SQL DACPAC',
    defaultStepName: 'Deploy DACPAC',
    scriptPath: '00 Generic\\DeployDacpac.ps1',
    category: 'SQL & Database',
    description:
      'Publishes a SQL Server DACPAC package using SqlPackage and the environment DACPAC publish profile.',
    parameters: [
      {
        name: 'dacpacName',
        label: 'DACPAC file name (dacpacName)',
        placeholder: 'e.g. MyDatabase.dacpac',
        required: true,
        helperText: 'DACPAC filename located in the build drop folder.'
      }
    ],
    requiredEnvironmentProperties: [
      'SQLPackagePath',
      'DACPACPublishProfile',
      'DACPACBlackList',
      'DACPACEnvironmentPostSQL',
      'DACPACRollbackMode'
    ]
  },
  {
    id: 'deploy-dacpac-azure',
    name: 'Deploy Azure SQL DACPAC',
    defaultStepName: 'Deploy Azure SQL DACPAC',
    scriptPath: '00 Generic\\DeployDacpacAzure.ps1',
    category: 'SQL & Database',
    description:
      'Publishes a SQL DACPAC package to an Azure SQL target using the environment publish profile.',
    parameters: [
      {
        name: 'dacpacName',
        label: 'DACPAC file name (dacpacName)',
        placeholder: 'e.g. MyAzureDatabase.dacpac',
        required: true,
        helperText: 'DACPAC filename located in the build drop folder.'
      }
    ],
    requiredEnvironmentProperties: [
      'SQLPackagePath',
      'DACPACPublishProfile',
      'DACPACBlackList',
      'DACPACEnvironmentPostSQL',
      'DACPACRollbackMode'
    ]
  },
  {
    id: 'apply-permissions',
    name: 'Apply SQL Database Permissions',
    defaultStepName: 'Apply SQL Permissions',
    scriptPath: '00 Generic\\ApplyPermissions.ps1',
    category: 'SQL & Database',
    description:
      'Applies database role and account permissions associated with the specified DACPAC.',
    parameters: [
      {
        name: 'dacpacName',
        label: 'DACPAC file name (dacpacName)',
        placeholder: 'e.g. MyDatabase.dacpac',
        required: true,
        helperText:
          'DACPAC filename identifying the target database permissions set.'
      }
    ],
    requiredEnvironmentProperties: [
      'SQLPackagePath',
      'DACPACPublishProfile',
      'DACPACBlackList',
      'DACPACEnvironmentPostSQL',
      'DACPACRollbackMode'
    ]
  },
  {
    id: 'restore-database',
    name: 'Restore SQL Database',
    defaultStepName: 'Restore Database',
    scriptPath: '00 Generic\\RestoreDatabase.ps1',
    category: 'SQL & Database',
    description:
      'Restores a SQL Server database from the configured restore source prior to DACPAC deployment.',
    parameters: [
      {
        name: 'dacpacName',
        label: 'DACPAC file name (dacpacName)',
        placeholder: 'e.g. MyDatabase.dacpac',
        required: true,
        helperText: 'DACPAC filename used to resolve the target database.'
      }
    ],
    requiredEnvironmentProperties: [
      'DropFolder',
      'EnvMgtDBServer',
      'EnvMgtDBName',
      'RestoreMode',
      'RestoreSource',
      'DORC_RestoreDBExePath',
      'DORC_DropDBExePath'
    ]
  },
  {
    id: 'snap-restore-database',
    name: 'Snap Restore SQL Database',
    defaultStepName: 'Snap Restore Database',
    scriptPath: '00 Generic\\SnapRestoreDatabase_V1.ps1',
    category: 'SQL & Database',
    description:
      'Performs a storage snapshot restore of the target SQL database and preserves permissions.',
    parameters: [
      {
        name: 'dacpacName',
        label: 'DACPAC file name (dacpacName)',
        placeholder: 'e.g. MyDatabase.dacpac',
        required: true
      }
    ],
    requiredEnvironmentProperties: [
      'DropFolder',
      'EnvMgtDBServer',
      'EnvMgtDBName',
      'RestoreMode',
      'RestoreSource',
      'DBPermsOutput'
    ]
  },
  {
    id: 'restore-database-commvault',
    name: 'Restore SQL Database (CommVault)',
    defaultStepName: 'Restore Database via CommVault',
    scriptPath: '00 Generic\\RestoreDatabaseCommVault.ps1',
    category: 'SQL & Database',
    description: 'Restores a SQL Server database from a CommVault backup copy.',
    parameters: [
      {
        name: 'dacpacName',
        label: 'DACPAC file name (dacpacName)',
        placeholder: 'e.g. MyDatabase.dacpac',
        required: true
      }
    ],
    requiredEnvironmentProperties: [
      'DropFolder',
      'EnvMgtDBServer',
      'EnvMgtDBName',
      'RestoreMode',
      'RestoreSource',
      'CommVaultApiURL'
    ]
  },
  {
    id: 'deploy-activebatch-v12',
    name: 'Deploy ActiveBatch V12 Jobs',
    defaultStepName: 'Deploy ActiveBatch Jobs',
    scriptPath: '00 Generic\\DeployActiveBatchV12.ps1',
    category: 'ActiveBatch',
    description:
      'Imports and configures ActiveBatch V12 job definitions from an XML file in the build drop.',
    parameters: [
      {
        name: 'XmlFile',
        label: 'ActiveBatch XML file (XmlFile)',
        placeholder: 'e.g. ActiveBatch.xml',
        required: true,
        helperText: 'XML definition filename to import.'
      },
      {
        name: 'app',
        label: 'Application folder (app)',
        placeholder: 'e.g. MyApp',
        required: false,
        helperText: 'Target application folder in ActiveBatch.'
      },
      {
        name: 'subFolder',
        label: 'Subfolder (subFolder)',
        placeholder: 'Optional subfolder name',
        required: false,
        helperText:
          'Optional child folder under the ActiveBatch application folder.'
      }
    ],
    requiredEnvironmentProperties: ['ABDestinationPath']
  },
  {
    id: 'deploy-activebatch-shared-objects',
    name: 'Deploy ActiveBatch Shared Objects',
    defaultStepName: 'Deploy ActiveBatch Shared Objects',
    scriptPath: '00 Generic\\DeployActiveBatchSharedObjects.ps1',
    category: 'ActiveBatch',
    description:
      'Deploys shared variables, queues, and credentials to ActiveBatch from a JSON config file.',
    parameters: [
      {
        name: 'JsonFile',
        label: 'Config JSON file (JsonFile)',
        placeholder: 'e.g. AB12.APP.ENV.config.json',
        required: true
      },
      {
        name: 'app',
        label: 'Application name (app)',
        placeholder: 'Optional app identifier',
        required: false
      }
    ],
    requiredEnvironmentProperties: ['ABDestinationPath']
  },
  {
    id: 'install-activebatch-v12-agent',
    name: 'Install ActiveBatch V12 Agent',
    defaultStepName: 'Install ActiveBatch V12 Agent',
    scriptPath: '00 Generic\\InstallActiveBatchV12Agent.ps1',
    category: 'ActiveBatch',
    description:
      'Installs the ActiveBatch V12 execution agent MSI on target environment servers.',
    parameters: [
      {
        name: 'msiName',
        label: 'Agent MSI name (msiName)',
        placeholder: 'e.g. ActiveBatch.msi',
        required: true
      },
      {
        name: 'productNames',
        label: 'Installed product name (productNames)',
        placeholder: 'e.g. ActiveBatch V12',
        required: true
      }
    ],
    requiredEnvironmentProperties: [
      'AB_Agent_V12_InstallFolder',
      'AB_Agent_UserName',
      'EnvMgtDBServer',
      'EnvMgtDBName'
    ]
  },
  {
    id: 'control-services-startup-type',
    name: 'Configure Windows Service Startup Type',
    defaultStepName: 'Configure Service Startup Type',
    scriptPath: '00 Generic\\ControlServicesStartupType.ps1',
    category: 'Windows Services',
    description:
      'Sets the Windows service startup mode (Automatic, Manual, Disabled) across target server types.',
    parameters: [
      {
        name: 'ServerTypes',
        label: 'Target server types (ServerTypes)',
        placeholder: 'e.g. APP',
        required: true
      },
      {
        name: 'Services',
        label: 'Service names (Services)',
        placeholder: 'e.g. MyAppService',
        required: true
      },
      {
        name: 'ServiceStartupType',
        label: 'Startup type (ServiceStartupType)',
        placeholder: 'Automatic, Manual, or Disabled',
        required: false,
        options: ['Automatic', 'Manual', 'Disabled']
      }
    ]
  },
  {
    id: 'remove-product',
    name: 'Uninstall Windows Product',
    defaultStepName: 'Remove Installed Product',
    scriptPath: '00 Generic\\RemoveProduct.ps1',
    category: 'Application & MSI',
    description:
      'Uninstalls an existing Windows product matching the specified display name pattern.',
    parameters: [
      {
        name: 'AppToRemove',
        label: 'Product name pattern (AppToRemove)',
        placeholder: 'e.g. MyLegacyService*',
        required: true,
        helperText: 'Wildcard-capable product display name to uninstall.'
      }
    ]
  },
  {
    id: 'copy-and-extract-artifacts',
    name: 'Copy and Extract Build Artifacts',
    defaultStepName: 'Copy and Extract Artifacts',
    scriptPath: '00 Generic\\CopyAndExtractArtifacts.ps1',
    category: 'Application & MSI',
    description:
      'Copies build artifacts from the drop location and extracts them to the target destination path.',
    parameters: [
      {
        name: 'buildName',
        label: 'Build name (buildName)',
        placeholder: 'e.g. MyComponentBuild',
        required: false
      },
      {
        name: 'artifactName',
        label: 'Artifact name (artifactName)',
        placeholder: 'e.g. drop',
        required: false
      },
      {
        name: 'destinationFolder',
        label: 'Destination folder (destinationFolder)',
        placeholder: 'e.g. ConsoleApps\\MyService',
        required: false
      }
    ],
    requiredEnvironmentProperties: [
      'DropFolder',
      'DestinationToCopyPath',
      'DeploymentServiceAccount',
      'DeploymentServiceAccountPassword'
    ]
  },
  {
    id: 'deploy-generic-sf-service',
    name: 'Deploy Service Fabric Application',
    defaultStepName: 'Deploy Service Fabric Service',
    scriptPath: '00 Generic\\DeployGenericSFService.ps1',
    category: 'Application & MSI',
    description:
      'Deploys and upgrades a packaged Service Fabric service definition.',
    parameters: [
      {
        name: 'sfSvcDef',
        label: 'Service Fabric definition (sfSvcDef)',
        placeholder: 'AppName;AppType;PkgName;1',
        required: true
      },
      {
        name: 'sfUsersEncryptDef',
        label: 'Encryption definition (sfUsersEncryptDef)',
        placeholder: 'Optional encryption parameters',
        required: false
      }
    ]
  },
  {
    id: 'deploy-sccm',
    name: 'Deploy Client MSI via SCCM',
    defaultStepName: 'Deploy SCCM Client Package',
    scriptPath: '00 Generic\\DeploySCCM.ps1',
    category: 'Application & MSI',
    description: 'Publishes a client MSI installer package through SCCM.',
    parameters: [
      {
        name: 'ClientMSIName',
        label: 'Client MSI file name (ClientMSIName)',
        placeholder: 'e.g. Deploy.ClientInstaller.msi',
        required: true
      },
      {
        name: 'productNames',
        label: 'Product name (productNames)',
        placeholder: 'Optional product display name',
        required: false
      }
    ]
  },
  {
    id: 'deploy-intune-app',
    name: 'Deploy Client App via Intune',
    defaultStepName: 'Deploy Intune Application',
    scriptPath: '00 Generic\\DeployIntuneApp.ps1',
    category: 'Application & MSI',
    description:
      'Packages and publishes a client MSI installer to Microsoft Intune.',
    parameters: [
      {
        name: 'ClientMSIName',
        label: 'Client MSI file name (ClientMSIName)',
        placeholder: 'e.g. Deploy.ClientInstaller.msi',
        required: true
      }
    ],
    requiredEnvironmentProperties: ['IntuneAppSecret', 'DropFolder']
  },
  {
    id: 'install-chocolatey',
    name: 'Install Chocolatey Package Manager',
    defaultStepName: 'Install Chocolatey',
    scriptPath: '00 Generic\\InstallChocolatey.ps1',
    category: 'Server & Patching',
    description:
      'Installs or updates Chocolatey on target servers matching BaseBuildServerType.',
    parameters: [
      {
        name: 'BaseBuildServerType',
        label: 'Target server type (BaseBuildServerType)',
        placeholder: 'e.g. APP',
        required: false,
        helperText: 'Can be supplied here or via environment properties.'
      }
    ]
  },
  {
    id: 'install-choco-packages',
    name: 'Install Chocolatey Packages',
    defaultStepName: 'Install Choco Packages',
    scriptPath: '00 Generic\\InstallChocoPackages.ps1',
    category: 'Server & Patching',
    description:
      'Installs configured Chocolatey packages from the internal package repository.',
    parameters: [
      {
        name: 'BaseBuildServerType',
        label: 'Target server type (BaseBuildServerType)',
        placeholder: 'e.g. APP',
        required: false
      },
      {
        name: 'Choco_InstallParams',
        label: 'Install parameters (Choco_InstallParams)',
        placeholder: 'Optional package install arguments',
        required: false
      }
    ],
    requiredEnvironmentProperties: ['Choco_PackageRepo', 'Choco_BinarySource']
  },
  {
    id: 'windows-features',
    name: 'Enable Windows Server Features',
    defaultStepName: 'Configure Windows Features',
    scriptPath: '00 Generic\\WindowsFeatures.ps1',
    category: 'Server & Patching',
    description:
      'Installs required Windows Server roles and features defined in BaseBuildFeatures.',
    parameters: [
      {
        name: 'BaseBuildServerType',
        label: 'Target server type (BaseBuildServerType)',
        placeholder: 'e.g. WEB',
        required: false
      }
    ],
    requiredEnvironmentProperties: [
      'CoreCodeSxSFolder',
      'BaseBuildFeatures',
      'DeploymentServiceAccount',
      'DeploymentServiceAccountPassword'
    ]
  },
  {
    id: 'install-dotnet-framework',
    name: 'Install .NET Framework',
    defaultStepName: 'Install .NET Framework',
    scriptPath: '00 Generic\\Install .Net Framework.ps1',
    category: 'Server & Patching',
    description:
      'Installs the specified .NET Framework version onto environment servers.',
    parameters: [
      {
        name: 'BaseBuildDotNetVersion',
        label: '.NET Framework version (BaseBuildDotNetVersion)',
        placeholder: 'e.g. 4.8',
        required: false
      }
    ],
    requiredEnvironmentProperties: [
      'EnvMgtDBName',
      'EnvMgtDBServer',
      'BaseBuildDotNetVersion',
      'BaseBuildDotNetInstallPath'
    ]
  },
  {
    id: 'recycle-env',
    name: 'Recycle Environment Servers',
    defaultStepName: 'Recycle Environment',
    scriptPath: '00 Generic\\RecycleEnv.ps1',
    category: 'Server & Patching',
    description:
      'Performs a controlled recycle/reboot action across target environment servers.',
    parameters: [
      {
        name: 'RecycleEnv_ServerType',
        label: 'Target server type (RecycleEnv_ServerType)',
        placeholder: 'e.g. APP',
        required: false
      }
    ],
    requiredEnvironmentProperties: [
      'RecycleEnv_Action',
      'EnvMgtDBServer',
      'EnvMgtDBName'
    ]
  },
  {
    id: 'vmm-software-center-patching',
    name: 'VMM Software Center Patching',
    defaultStepName: 'Run Software Center Patching',
    scriptPath: '00 Generic\\Invoke-VmmSoftwareCenterPatching.ps1',
    category: 'Server & Patching',
    description:
      'Executes VMM / Software Center OS patching across environment virtual machines.',
    parameters: [
      {
        name: 'ExecutionMode',
        label: 'Execution mode (ExecutionMode)',
        placeholder: 'e.g. Central',
        required: true
      },
      {
        name: 'OwningProjectId',
        label: 'Owning project ID (OwningProjectId)',
        placeholder: 'Optional numeric project ID',
        required: false
      }
    ]
  },
  {
    id: 'deploy-ssis-packages',
    name: 'Deploy SSIS Packages (On-Premises)',
    defaultStepName: 'Deploy SSIS Packages',
    scriptPath: '00 Generic\\DeploySSISPackages.ps1',
    category: 'SSIS & SSRS',
    description:
      'Deploys SQL Server Integration Services (SSIS) packages to an on-premises SSIS catalog.',
    parameters: [
      {
        name: 'SSIS_Project',
        label: 'SSIS project name (SSIS_Project)',
        placeholder: 'e.g. MarketDataETL',
        required: false
      }
    ],
    requiredEnvironmentProperties: ['SSIS_Server', 'SSIS_Db']
  },
  {
    id: 'deploy-ssis-packages-azure',
    name: 'Deploy SSIS Packages (Azure)',
    defaultStepName: 'Deploy Azure SSIS Packages',
    scriptPath: '00 Generic\\DeploySSISPackagesAzure.ps1',
    category: 'SSIS & SSRS',
    description: 'Deploys SSIS ISPAC packages to an Azure-hosted SSIS catalog.',
    parameters: [
      {
        name: 'SSIS_Project',
        label: 'SSIS project name (SSIS_Project)',
        placeholder: 'e.g. PFE Azure Data',
        required: true
      }
    ],
    requiredEnvironmentProperties: ['SSIS_Server_Azure', 'SSIS_Db_Azure']
  },
  {
    id: 'configure-ssis-projects',
    name: 'Configure SSIS Project Environments',
    defaultStepName: 'Configure SSIS Projects',
    scriptPath: '00 Generic\\ConfigureSSISProjects.ps1',
    category: 'SSIS & SSRS',
    description:
      'Configures SSIS catalog environment references and parameters for deployed SSIS projects.',
    parameters: [
      {
        name: 'SSIS_Project',
        label: 'SSIS project name (SSIS_Project)',
        placeholder: 'Optional SSIS project filter',
        required: false
      }
    ],
    requiredEnvironmentProperties: ['SSIS_Server', 'SSIS_Db']
  },
  {
    id: 'deploy-ssrs-reports',
    name: 'Deploy SSRS Reports',
    defaultStepName: 'Deploy SSRS Reports',
    scriptPath: '00 Generic\\DeploySSRSReportsNew.ps1',
    category: 'SSIS & SSRS',
    description:
      'Publishes SQL Server Reporting Services (SSRS) report definitions and data sources.',
    parameters: [],
    requiredEnvironmentProperties: [
      'SSRS_RootFolder',
      'SSRS_ReportingServerURI'
    ]
  },
  {
    id: 'request-install-bind-cert',
    name: 'Request, Install & Bind TLS Certificate',
    defaultStepName: 'Install & Bind Certificate',
    scriptPath: '00 Generic\\RequestInstallBindCert.ps1',
    category: 'Messaging & Certificates',
    description:
      'Requests a TLS certificate, installs it into the certificate store, and binds it to the target port.',
    parameters: [
      {
        name: 'CertFriendlyName',
        label: 'Certificate friendly name (CertFriendlyName)',
        placeholder: 'e.g. My Service API',
        required: true
      },
      {
        name: 'BindingPort',
        label: 'HTTPS binding port (BindingPort)',
        placeholder: 'e.g. 5257',
        required: true
      },
      {
        name: 'dnsNames',
        label: 'DNS names (dnsNames)',
        placeholder: 'Optional SAN DNS names',
        required: false
      }
    ],
    requiredEnvironmentProperties: [
      'CertTemplate',
      'CertSubjectName',
      'CertDnsNames',
      'CertServerTag'
    ]
  },
  {
    id: 'configure-rabbitmq',
    name: 'Configure RabbitMQ Exchanges & Queues',
    defaultStepName: 'Configure RabbitMQ',
    scriptPath: '00 Generic\\ConfigureRabbitMQ.ps1',
    category: 'Messaging & Certificates',
    description:
      'Configures RabbitMQ virtual hosts, exchanges, queues, and bindings via the management API.',
    parameters: [],
    requiredEnvironmentProperties: [
      'DropFolder',
      'RMQConfigFileName',
      'RMQConfigUsername',
      'RMQConfigPassword',
      'RMQManagementApiUrl'
    ]
  },
  {
    id: 'apply-rabbitmq-config',
    name: 'Apply RabbitMQ Definitions',
    defaultStepName: 'Apply RabbitMQ Config',
    scriptPath: '00 Generic\\ApplyRabbitMQConfig.ps1',
    category: 'Messaging & Certificates',
    description:
      'Applies RabbitMQ broker definitions from the drop folder to the management API.',
    parameters: [],
    requiredEnvironmentProperties: [
      'DropFolder',
      'RMQManagementApiUrl',
      'RMQConfigUsername',
      'RMQConfigPassword'
    ]
  }
];

export const GENERIC_SCRIPT_CATEGORIES = [
  'All',
  'Application & MSI',
  'SQL & Database',
  'Windows Services',
  'ActiveBatch',
  'Server & Patching',
  'SSIS & SSRS',
  'Messaging & Certificates'
] as const;

export function normalizeGenericPath(path: string): string {
  return path.trim().replace(/\//g, '\\').toLowerCase();
}

export function findGenericScriptTemplate(
  scriptPath: string | undefined | null
): GenericScriptTemplate | undefined {
  if (!scriptPath) return undefined;
  const parsed = parseScriptPathValue(scriptPath);
  const normalized = normalizeGenericPath(parsed.scriptPath);
  return GENERIC_SCRIPT_TEMPLATES.find(
    template => normalizeGenericPath(template.scriptPath) === normalized
  );
}

export function parseScriptPathValue(
  raw: string | undefined | null
): ParsedScriptPath {
  const trimmed = (raw ?? '').trim();
  if (!trimmed) {
    return { scriptPath: '', isJsonPath: false, arguments: [] };
  }
  if (trimmed.startsWith('{')) {
    try {
      const parsed = JSON.parse(trimmed) as {
        ScriptPath?: string;
        scriptPath?: string;
        GenericArguments?: Array<Record<string, unknown>>;
        genericArguments?: Array<Record<string, unknown>>;
      };
      const scriptPath =
        typeof parsed.ScriptPath === 'string'
          ? parsed.ScriptPath
          : typeof parsed.scriptPath === 'string'
            ? parsed.scriptPath
            : '';
      const rawArgs = Array.isArray(parsed.GenericArguments)
        ? parsed.GenericArguments
        : Array.isArray(parsed.genericArguments)
          ? parsed.genericArguments
          : [];
      if (scriptPath || rawArgs.length > 0) {
        const args: Array<{ key: string; value: string }> = [];
        for (const entry of rawArgs) {
          if (entry && typeof entry === 'object') {
            for (const [key, value] of Object.entries(entry)) {
              args.push({
                key,
                value: value == null ? '' : String(value)
              });
            }
          }
        }
        return {
          scriptPath,
          isJsonPath: true,
          arguments: args
        };
      }
    } catch {
      // Fall back to treating invalid or non-GenericArguments JSON as a raw path
    }
  }
  return {
    scriptPath: trimmed,
    isJsonPath: false,
    arguments: []
  };
}

export function serializeScriptPathValue(
  scriptPath: string,
  args: Array<{ key: string; value: string }>,
  forceJson = false
): string {
  const cleanPath = scriptPath.trim();
  // Keep arguments whose value is blank: the Monitor adds every GenericArgument
  // as a script property (overriding the environment property of the same
  // name), so an explicit "" differs from "absent". Callers that pre-populate
  // parameters the user never filled must drop them before serializing.
  const nonEmptyArgs = args.filter(arg => arg.key.trim().length > 0);
  if (!forceJson && nonEmptyArgs.length === 0) {
    return cleanPath;
  }
  return JSON.stringify({
    ScriptPath: cleanPath,
    GenericArguments: nonEmptyArgs.map(arg => ({
      [arg.key.trim()]: arg.value
    }))
  });
}
