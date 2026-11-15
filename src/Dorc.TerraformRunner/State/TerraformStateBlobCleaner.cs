using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Runner.Logger;
using Dorc.Terraform.Catalog;

namespace Dorc.TerraformRunner.State
{
    public sealed class TerraformStateBlobCleaner : ITerraformStateBlobCleaner
    {
        public const string StorageAccountPropertyName = "TerraformStateStorageAccount";
        public const string ContainerNamePropertyName = "TerraformStateContainerName";
        public const string KeyPropertyName = "TerraformStateKey";

        internal static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(15);
        internal static readonly TimeSpan ImdsTimeout = TimeSpan.FromSeconds(3);
        internal static readonly TimeSpan CliTimeout = TimeSpan.FromSeconds(8);
        internal static string ResolveAzCliExecutableForPlatform(bool isWindows) =>
            isWindows ? "az.cmd" : "az";
        internal static Func<string> DefaultAzCliExecutableResolver { get; set; } =
            () => ResolveAzCliExecutableForPlatform(OperatingSystem.IsWindows());

        private static readonly HttpClient SharedHttpClient = new();

        private readonly IRunnerLogger _logger;
        private readonly HttpClient _httpClient;
        private readonly Func<string, string, string, TerraformArmCredentials?, CancellationToken, Task<bool>> _deleteBlobAsync;
        private readonly Func<string, string?> _environmentVariableGetter;
        private readonly Func<CancellationToken, Task<string?>>? _cliTokenProvider;

        public TerraformStateBlobCleaner(
            IRunnerLogger logger,
            Func<string, string, string, TerraformArmCredentials?, CancellationToken, Task<bool>>? deleteBlobAsync = null,
            HttpClient? httpClient = null,
            Func<string, string?>? environmentVariableGetter = null,
            Func<CancellationToken, Task<string?>>? cliTokenProvider = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? SharedHttpClient;
            _deleteBlobAsync = deleteBlobAsync ?? DefaultDeleteBlobAsync;
            _environmentVariableGetter = environmentVariableGetter ?? Environment.GetEnvironmentVariable;
            _cliTokenProvider = cliTokenProvider;
        }

        public TerraformStateBlobCleaner(
            IRunnerLogger logger,
            HttpClient httpClient)
            : this(logger, null, httpClient, null, null)
        {
        }

        public async Task<bool> DeleteStateBlobIfConfiguredAsync(
            ScriptGroup? scriptGroup,
            CancellationToken cancellationToken = default)
        {
            var storageAccount = !string.IsNullOrWhiteSpace(scriptGroup?.TerraformStateStorageAccount)
                ? scriptGroup!.TerraformStateStorageAccount.Trim()
                : null;
            var containerName = !string.IsNullOrWhiteSpace(scriptGroup?.TerraformStateContainerName)
                ? scriptGroup!.TerraformStateContainerName.Trim()
                : null;
            var key = !string.IsNullOrWhiteSpace(scriptGroup?.TerraformStateKey)
                ? scriptGroup!.TerraformStateKey.Trim()
                : null;

            if (storageAccount is null || containerName is null || key is null)
            {
                _logger.Information(
                    "Skipping Terraform state blob cleanup: backend configuration is not present.");
                return false;
            }

            if (!IsValidStateBlobKey(key))
            {
                _logger.Warning(
                    $"Refusing to delete Terraform state blob with invalid state key '{key}'. Key must match '{{project}}/{{component}}/{{env}}.tfstate' without path traversal segments.");
                return false;
            }

            try
            {
                var credentials = TerraformArmCredentials.Resolve(scriptGroup!.CommonProperties);
                var deleted = await _deleteBlobAsync(storageAccount, containerName, key, credentials, cancellationToken);
                if (deleted)
                {
                    _logger.Information(
                        $"Deleted empty Terraform state blob '{key}' from container '{containerName}' in storage account '{storageAccount}'.");
                }
                else
                {
                    _logger.Information(
                        $"Terraform state blob '{key}' in container '{containerName}' (storage account '{storageAccount}') was not deleted.");
                }

                return deleted;
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    $"Failed to delete Terraform state blob '{key}' from '{storageAccount}/{containerName}': {ex.Message}");
                return false;
            }
        }

        public static bool IsValidStateBlobKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)
                || key.Contains('\\', StringComparison.Ordinal)
                || !key.EndsWith(".tfstate", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var segments = key.Split('/');
            if (segments.Length < 3)
            {
                return false;
            }

            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment)
                    || segment == "."
                    || segment.Contains("..", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return segments[^1].Length > ".tfstate".Length;
        }

        private async Task<bool> DefaultDeleteBlobAsync(
            string storageAccount,
            string containerName,
            string key,
            TerraformArmCredentials? credentials,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(CleanupTimeout);
            var boundedToken = timeoutCts.Token;

            var accessToken = credentials is not null
                ? await AcquireClientSecretAccessTokenAsync(
                      credentials.TenantId,
                      credentials.ClientId,
                      credentials.ClientSecret,
                      boundedToken)
                  ?? await AcquireAmbientAccessTokenAsync(boundedToken)
                : await AcquireAmbientAccessTokenAsync(boundedToken);

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                _logger.Warning(
                    "Unable to acquire Azure Storage access token for state blob cleanup via SPN, Workload Identity, Managed Identity, or Azure CLI; failing state blob cleanup.");
                return false;
            }

            var escapedKey = Uri.EscapeDataString(key).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
            var blobUri = $"https://{storageAccount}.blob.core.windows.net/{Uri.EscapeDataString(containerName)}/{escapedKey}";

            return await InspectLeaseAndDeleteBlobAsync(
                storageAccount,
                containerName,
                key,
                blobUri,
                accessToken,
                boundedToken);
        }

        private async Task<bool> InspectLeaseAndDeleteBlobAsync(
            string storageAccount,
            string containerName,
            string key,
            string blobUri,
            string accessToken,
            CancellationToken cancellationToken)
        {
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, blobUri);
            getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            getRequest.Headers.TryAddWithoutValidation("x-ms-version", "2021-12-02");

            using var getResponse = await _httpClient.SendAsync(getRequest, cancellationToken);
            if (getResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return true;
            }

            if (!getResponse.IsSuccessStatusCode)
            {
                _logger.Warning(
                    $"Azure Blob Storage GET for '{storageAccount}/{containerName}/{key}' returned HTTP {(int)getResponse.StatusCode}.");
                return false;
            }

            var stateContent = await getResponse.Content.ReadAsStringAsync(cancellationToken);
            if (HasRemainingResourcesInState(stateContent))
            {
                _logger.Warning(
                    $"Refusing to delete Terraform state blob '{storageAccount}/{containerName}/{key}' because state still contains active resources.");
                return false;
            }

            var etag = getResponse.Headers.ETag?.Tag;
            string? leaseId = null;
            if (!string.IsNullOrWhiteSpace(etag))
            {
                using var leaseRequest = new HttpRequestMessage(HttpMethod.Put, $"{blobUri}?comp=lease");
                leaseRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                leaseRequest.Headers.TryAddWithoutValidation("x-ms-version", "2021-12-02");
                leaseRequest.Headers.TryAddWithoutValidation("x-ms-lease-action", "acquire");
                leaseRequest.Headers.TryAddWithoutValidation("x-ms-lease-duration", "15");
                leaseRequest.Headers.TryAddWithoutValidation("If-Match", etag);

                using var leaseResponse = await _httpClient.SendAsync(leaseRequest, cancellationToken);
                if (!leaseResponse.IsSuccessStatusCode)
                {
                    _logger.Warning(
                        $"Unable to acquire lease on Terraform state blob '{storageAccount}/{containerName}/{key}' before deletion (HTTP {(int)leaseResponse.StatusCode}).");
                    return false;
                }

                if (leaseResponse.Headers.TryGetValues("x-ms-lease-id", out var leaseValues))
                {
                    leaseId = leaseValues.FirstOrDefault();
                }
            }

            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, blobUri);
            deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            deleteRequest.Headers.TryAddWithoutValidation("x-ms-version", "2021-12-02");
            deleteRequest.Headers.TryAddWithoutValidation("x-ms-delete-snapshots", "include");
            if (!string.IsNullOrWhiteSpace(etag))
            {
                deleteRequest.Headers.TryAddWithoutValidation("If-Match", etag);
            }
            if (!string.IsNullOrWhiteSpace(leaseId))
            {
                deleteRequest.Headers.TryAddWithoutValidation("x-ms-lease-id", leaseId);
            }

            using var deleteResponse = await _httpClient.SendAsync(deleteRequest, cancellationToken);
            if (deleteResponse.StatusCode is HttpStatusCode.OK
                or HttpStatusCode.Accepted
                or HttpStatusCode.NoContent
                or HttpStatusCode.NotFound)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(leaseId))
            {
                using var releaseRequest = new HttpRequestMessage(HttpMethod.Put, $"{blobUri}?comp=lease");
                releaseRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                releaseRequest.Headers.TryAddWithoutValidation("x-ms-version", "2021-12-02");
                releaseRequest.Headers.TryAddWithoutValidation("x-ms-lease-action", "release");
                releaseRequest.Headers.TryAddWithoutValidation("x-ms-lease-id", leaseId);
                using var _ = await _httpClient.SendAsync(releaseRequest, cancellationToken);
            }

            _logger.Warning(
                $"Azure Blob Storage DELETE for '{storageAccount}/{containerName}/{key}' returned HTTP {(int)deleteResponse.StatusCode}.");
            return false;
        }

        public static bool HasRemainingResourcesInState(string? stateContent)
        {
            if (string.IsNullOrWhiteSpace(stateContent))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(stateContent);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("resources", out var resources)
                    && resources.ValueKind == JsonValueKind.Array
                    && resources.GetArrayLength() > 0)
                {
                    return true;
                }

                return false;
            }
            catch (JsonException)
            {
                return true;
            }
        }

        private async Task<string?> AcquireClientSecretAccessTokenAsync(
            string tenantId,
            string clientId,
            string clientSecret,
            CancellationToken cancellationToken)
        {
            var tokenEndpoint = $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenantId.Trim())}/oauth2/v2.0/token";
            using var tokenRequestContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId.Trim(),
                ["client_secret"] = clientSecret,
                ["scope"] = "https://storage.azure.com/.default",
            });

            using var tokenResponse = await _httpClient.PostAsync(tokenEndpoint, tokenRequestContent, cancellationToken);
            return await TryReadAccessTokenAsync(tokenResponse, cancellationToken);
        }

        internal async Task<string?> AcquireAmbientAccessTokenAsync(
            CancellationToken cancellationToken)
        {
            var directToken = FirstNonEmptyEnv("ARM_ACCESS_TOKEN", "AZURE_STORAGE_AUTH_TOKEN");
            if (directToken is not null)
            {
                return directToken;
            }

            var tenantId = FirstNonEmptyEnv("ARM_TENANT_ID", "AZURE_TENANT_ID");
            var clientId = FirstNonEmptyEnv("ARM_CLIENT_ID", "AZURE_CLIENT_ID");
            var clientSecret = FirstNonEmptyEnv("ARM_CLIENT_SECRET", "AZURE_CLIENT_SECRET");

            if (tenantId is not null && clientId is not null && clientSecret is not null)
            {
                var spnToken = await AcquireClientSecretAccessTokenAsync(tenantId, clientId, clientSecret, cancellationToken);
                if (spnToken is not null)
                {
                    return spnToken;
                }
            }

            var directOidcAssertion = FirstNonEmptyEnv("ARM_OIDC_TOKEN", "AZURE_FEDERATED_TOKEN");
            var federatedTokenFile = FirstNonEmptyEnv("AZURE_FEDERATED_TOKEN_FILE", "ARM_OIDC_TOKEN_FILE_PATH");
            if (tenantId is not null && clientId is not null && (directOidcAssertion is not null || federatedTokenFile is not null))
            {
                var oidcToken = await TryAcquireOidcAccessTokenAsync(
                    tenantId,
                    clientId,
                    directOidcAssertion,
                    federatedTokenFile,
                    cancellationToken);
                if (oidcToken is not null)
                {
                    return oidcToken;
                }
            }

            var identityEndpoint = FirstNonEmptyEnv("IDENTITY_ENDPOINT", "MSI_ENDPOINT");
            var identityHeader = FirstNonEmptyEnv("IDENTITY_HEADER", "MSI_SECRET");
            if (identityEndpoint is not null && identityHeader is not null)
            {
                var separator = identityEndpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                var requestUri = $"{identityEndpoint}{separator}resource=https://storage.azure.com/&api-version=2019-08-01";

                using var miRequest = new HttpRequestMessage(HttpMethod.Get, requestUri);
                miRequest.Headers.TryAddWithoutValidation("X-IDENTITY-HEADER", identityHeader);

                using var miResponse = await _httpClient.SendAsync(miRequest, cancellationToken);
                var miToken = await TryReadAccessTokenAsync(miResponse, cancellationToken);
                if (miToken is not null)
                {
                    return miToken;
                }
            }

            var imdsUri = "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fstorage.azure.com%2F";
            if (clientId is not null)
            {
                imdsUri += $"&client_id={Uri.EscapeDataString(clientId)}";
            }

            try
            {
                using var imdsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                imdsCts.CancelAfter(ImdsTimeout);
                using var imdsRequest = new HttpRequestMessage(HttpMethod.Get, imdsUri);
                imdsRequest.Headers.TryAddWithoutValidation("Metadata", "true");

                using var imdsResponse = await _httpClient.SendAsync(imdsRequest, imdsCts.Token);
                var imdsToken = await TryReadAccessTokenAsync(imdsResponse, imdsCts.Token);
                if (!string.IsNullOrWhiteSpace(imdsToken))
                {
                    return imdsToken;
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.Information($"IMDS token probe did not succeed: {ex.Message}");
            }

            if (_cliTokenProvider is not null)
            {
                var providedToken = await _cliTokenProvider(cancellationToken);
                if (!string.IsNullOrWhiteSpace(providedToken))
                {
                    return providedToken;
                }
            }

            var cliExe = FirstNonEmptyEnv("DORC_AZ_CLI_PATH", "AZ_CLI_PATH") ?? DefaultAzCliExecutableResolver();
            return await TryAcquireCliTokenFromProcessAsync(
                cliExe,
                "account get-access-token --resource https://storage.azure.com/ --query accessToken -o tsv",
                cancellationToken);
        }

        private async Task<string?> TryAcquireOidcAccessTokenAsync(
            string tenantId,
            string clientId,
            string? directOidcAssertion,
            string? federatedTokenFile,
            CancellationToken cancellationToken)
        {
            string? assertion = directOidcAssertion;
            if (assertion is null)
            {
                if (!File.Exists(federatedTokenFile!))
                {
                    _logger.Warning(
                        $"Azure Workload Identity federated token file '{federatedTokenFile}' was not found for state blob cleanup.");
                    return null;
                }

                assertion = (await File.ReadAllTextAsync(federatedTokenFile!, cancellationToken)).Trim();
                if (string.IsNullOrWhiteSpace(assertion))
                {
                    _logger.Warning(
                        $"Azure Workload Identity federated token file '{federatedTokenFile}' is empty.");
                    return null;
                }
            }

            var tokenEndpoint = $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenantId)}/oauth2/v2.0/token";
            using var tokenRequestContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
                ["client_assertion"] = assertion,
                ["scope"] = "https://storage.azure.com/.default",
            });

            using var tokenResponse = await _httpClient.PostAsync(tokenEndpoint, tokenRequestContent, cancellationToken);
            return await TryReadAccessTokenAsync(tokenResponse, cancellationToken);
        }

        public static async Task<string?> TryAcquireCliTokenFromProcessAsync(
            string fileName,
            string arguments,
            CancellationToken cancellationToken)
        {
            Process? process = null;
            try
            {
                using var psiCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                psiCts.CancelAfter(CliTimeout);
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                process = Process.Start(startInfo)!;

                var output = await process.StandardOutput.ReadToEndAsync(psiCts.Token);
                await process.WaitForExitAsync(psiCts.Token);
                if (process.ExitCode != 0)
                {
                    return null;
                }

                var trimmed = output.Trim();
                return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
            }
            catch
            {
                if (process is not null && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                return null;
            }
            finally
            {
                process?.Dispose();
            }
        }

        private string? FirstNonEmptyEnv(string primaryName, string fallbackName)
        {
            var primary = _environmentVariableGetter(primaryName);
            if (!string.IsNullOrWhiteSpace(primary))
            {
                return primary.Trim();
            }

            var fallback = _environmentVariableGetter(fallbackName);
            return !string.IsNullOrWhiteSpace(fallback) ? fallback.Trim() : null;
        }

        private async Task<string?> TryReadAccessTokenAsync(
            HttpResponseMessage tokenResponse,
            CancellationToken cancellationToken)
        {
            if (!tokenResponse.IsSuccessStatusCode)
            {
                _logger.Warning(
                    $"Failed to acquire Azure Storage OAuth token for state blob cleanup (HTTP {(int)tokenResponse.StatusCode}).");
                return null;
            }

            var tokenPayload = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            string? accessToken = null;
            using (var tokenJson = JsonDocument.Parse(tokenPayload))
            {
                if (tokenJson.RootElement.TryGetProperty("access_token", out var tokenElement))
                {
                    accessToken = tokenElement.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                _logger.Warning(
                    "Failed to acquire Azure Storage OAuth token for state blob cleanup: response did not contain an access_token.");
                return null;
            }

            return accessToken;
        }
    }
}
