using Dorc.ApiModel;

namespace Dorc.Core.Configuration
{
    public interface IConfigurationSettings
    {
        string GetConfigurationDomainName();
        string GetConfigurationDomainNameIntra();
        string[] GetAllowedCorsLocations();
        string GetDorcConnectionString();
        TimeSpan? GetADUserCacheTimeSpan();
        string? GetAuthenticationScheme();
        string? GetOAuthAuthority();
        string? GetOAuthUiClientId();
        string? GetOAuthUiRequestedScopes();
        string? GetOAuthApiResourceName();
        string? GetOAuthApiGlobalScope();
        string? GetOnePasswordBaseUrl();
        string? GetOnePasswordApiKey();
        string? GetOnePasswordVaultId();
        string? GetOnePasswordItemId();
        string? GetIdentityServerClientId();
        string? GetOnePasswordIdentityServerApiSecretItemId();
        bool GetIsUseAdAsSearcher();
        bool GetIsUseAdSidsForAccessControl();

        string GetAzureEntraTenantId();
        string GetAzureEntraClientId();
        string GetAzureEntraClientSecret();

        string? GetGitHubToken();

        string GetAzureStorageAccountTenantId();
        string GetAzureStorageAccountClientId();
        string GetAzureStorageAccountClientSecret();
        string GetAzureStorageAccounUri();
        string GetAzureStorageAccountTerraformBlobsContainerName();

        bool GetPauseDeploymentEnabled();

        /// <summary>
        /// Whether confirming a Terraform plan requires an approver other than the person
        /// who submitted the request.
        ///
        /// Returns null when the setting is absent or unparseable. Callers must treat null
        /// as "enabled for production-tier requests, disabled otherwise" - deliberately NOT
        /// the codebase's usual bool.TryParse(...) &amp;&amp; enabled pattern, which resolves an
        /// absent key to false and would ship this control silently off.
        /// </summary>
        bool? GetTerraformSeparateApproverRequired();

        /// <summary>
        /// Whether a script path resolving outside the script root refuses the deployment, or is
        /// only reported. Absence means report.
        /// </summary>
        bool GetScriptPathEnforcementEnabled();

        /// <summary>
        /// How far script content verification is taken. Absence, or an unrecognised value,
        /// means <see cref="ScriptContentVerificationMode.Report"/>.
        /// </summary>
        ScriptContentVerificationMode GetScriptContentVerificationMode();

        /// <summary>
        /// Whether deployment credentials come from the secrets vault rather than DOrc's own
        /// configuration values. Absence means configuration values.
        /// </summary>
        bool GetDeploymentCredentialsFromVault();

        /// <summary>
        /// The vault item holding a deployment credential, by tier and by whether the username
        /// or the password is wanted. Null when unconfigured.
        /// </summary>
        string? GetDeploymentCredentialItemId(Security.DeploymentTier tier, bool forPassword);
        bool GetIsProduction();
    }
}
