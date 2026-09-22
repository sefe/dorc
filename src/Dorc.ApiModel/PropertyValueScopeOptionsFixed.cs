namespace Dorc.ApiModel
{
    public static class PropertyValueScopeOptionsFixed
    {
        // From the Deployment Engine
        public static string EnvironmentName => "EnvironmentName";
        public static string DropFolder => "DropFolder";
        public static string ScriptRoot => "ScriptRoot";
        public static string DeploymentLogDir => "DeploymentLogDir";
        public static string BuildNumber => "BuildNumber";
        public static string EnvOwner => "EnvOwner";
        public static string EnvOwnerEmails => "EnvOwnerEmails";

        // Need the config value to be inserted here
        public static string AllServers => "AllServers";
        public static string EnvironmentServers => "EnvironmentServers";
        public static string EndurFileShare => "EndurFileShare";
        public static string EndurConfigurationFile => "EndurConfigurationFile";
        public static string EndurDatabaseName => "EndurDatabaseName";
        public static string EndurDatabaseServer => "EndurDatabaseServer";
        public static string EnvironmentShortName => "EnvironmentShortName";
        public static string ReportingDatabaseName => "ReportingDatabaseName";
        public static string ReportingDatabaseServer => "ReportingDatabaseServer";
        public static string SsisPackageServer => "SsisPackageServer";
        public static string ExternalDatabaseName => "ExternalDatabaseName";
        public static string ExternalDatabaseServer => "ExternalDatabaseServer";
        public static string DatabasePermissions => "DatabasePermissions";
        public static string DbServer => "DbServer_";
        public static string DbName => "DbName_";
        public static string ServerNames => "ServerNames_";
        public static string RefDataApiUrl => "RefDataApiUrl";

        // Environment component collections (emitted only when items are attached)
        public static string EnvironmentContainers => "EnvironmentContainers";
        public static string EnvironmentCloudResources => "EnvironmentCloudResources";
        public static string EnvironmentApiRegistrations => "EnvironmentApiRegistrations";
        public static string ContainerNames => "ContainerNames_";
        public static string CloudResourceNames => "CloudResourceNames_";
        public static string ApiRegistrationNames => "ApiRegistrationNames_";

    }
}
