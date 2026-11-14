using System.Buffers;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dorc.Core.Secrets.OnePassword
{
    public class OnePasswordClient
    {
        private delegate T Utf8JsonParser<out T, in TState>(ReadOnlySpan<byte> utf8Json, TState state);

        private enum VaultItemCategory
        {
            Unknown,
            Login,
            ApiCredential
        }

        private readonly HttpClient _httpClient;

        public OnePasswordClient(string baseUrl, string apiKey)
        {
            if (string.IsNullOrEmpty(baseUrl))
            {
                throw new ArgumentNullException(nameof(baseUrl));
            }
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentNullException(nameof(apiKey));
            }

            _httpClient = new HttpClient()
            {
                BaseAddress = new Uri(baseUrl)
            };

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        internal OnePasswordClient(HttpClient httpClient)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            if (httpClient.BaseAddress == null)
            {
                throw new ArgumentException("HttpClient.BaseAddress must be configured.", nameof(httpClient));
            }

            _httpClient = httpClient;
        }

        public async Task<string> GetSecretValueAsync(string vaultId, string itemId)
        {
            OnePasswordItem? secretItem = await this.GetItemAsync(vaultId, itemId).ConfigureAwait(false);
            if (secretItem != null)
            {
                switch (secretItem.Category?.ToUpperInvariant())
                {
                    case "API_CREDENTIAL":
                        return SanitizeVaultValue(secretItem.GetFieldValue("credential"));
                    case "LOGIN":
                        return SanitizeVaultValue(secretItem.GetFieldValue("password"));
                    default:
                        return string.Empty;
                }
            }
            return string.Empty;
        }

        public Task<SecureString> GetSecureSecretAsync(
            string vaultId,
            string itemId,
            CancellationToken cancellationToken = default)
        {
            return ReadVaultItemBytesAsync(
                vaultId,
                itemId,
                state: false,
                static (utf8Json, _) => ParseSecureSecretFromUtf8Json(utf8Json),
                cancellationToken);
        }

        private async Task<T> ReadVaultItemBytesAsync<T, TState>(
            string vaultId,
            string itemId,
            TState state,
            Utf8JsonParser<T, TState> parseUtf8Json,
            CancellationToken cancellationToken)
        {
            Uri uri = BuildItemUri(vaultId, itemId);

            using CancellationTokenSource? timeoutCts = CreateTimeoutCancellationTokenSource(cancellationToken);
            CancellationToken effectiveToken = timeoutCts?.Token ?? cancellationToken;

            using HttpResponseMessage response = await _httpClient
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, effectiveToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Error fetching item from 1Password: {response.StatusCode}",
                    null,
                    response.StatusCode);
            }

            using Stream stream = await response.Content
                .ReadAsStreamAsync(effectiveToken)
                .ConfigureAwait(false);

            byte[] rentedBytes = ArrayPool<byte>.Shared.Rent(4096);
            int totalRead = 0;

            try
            {
                while (true)
                {
                    if (totalRead == rentedBytes.Length)
                    {
                        byte[] larger = ArrayPool<byte>.Shared.Rent(checked(rentedBytes.Length * 2));
                        rentedBytes.AsSpan(0, totalRead).CopyTo(larger);
                        CryptographicOperations.ZeroMemory(rentedBytes.AsSpan(0, totalRead));
                        ArrayPool<byte>.Shared.Return(rentedBytes);
                        rentedBytes = larger;
                    }

                    int bytesRead = await stream
                        .ReadAsync(rentedBytes.AsMemory(totalRead, rentedBytes.Length - totalRead), effectiveToken)
                        .ConfigureAwait(false);

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    totalRead += bytesRead;
                }

                return parseUtf8Json(rentedBytes.AsSpan(0, totalRead), state);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rentedBytes.AsSpan());
                ArrayPool<byte>.Shared.Return(rentedBytes);
            }
        }

        private Uri BuildItemUri(string vaultId, string itemId)
        {
            return new Uri($"{_httpClient.BaseAddress?.ToString().TrimEnd('/')}/v1/vaults/{vaultId}/items/{itemId}");
        }

        private CancellationTokenSource? CreateTimeoutCancellationTokenSource(CancellationToken cancellationToken)
        {
            TimeSpan timeout = _httpClient.Timeout;
            if (timeout != Timeout.InfiniteTimeSpan && timeout > TimeSpan.Zero)
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);
                return cts;
            }

            return cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
        }

        internal static SecureString ParseSecureSecretFromUtf8Json(ReadOnlySpan<byte> utf8Json)
        {
            string? targetField = ReadItemCategory(utf8Json) switch
            {
                VaultItemCategory.Login => "password",
                VaultItemCategory.ApiCredential => "credential",
                _ => null
            };
            if (targetField == null)
            {
                return CreateEmptyReadOnlySecureString();
            }

            var reader = new Utf8JsonReader(utf8Json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return CreateEmptyReadOnlySecureString();
            }

            bool foundIdField = false;
            bool hasIdValue = false;
            Utf8JsonReader idValueReader = default;

            bool foundLabelField = false;
            bool hasLabelValue = false;
            Utf8JsonReader labelValueReader = default;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                bool isFieldsProperty = reader.ValueTextEquals("fields"u8);
                if (!reader.Read())
                {
                    break;
                }

                if (!isFieldsProperty || reader.TokenType != JsonTokenType.StartArray)
                {
                    reader.Skip();
                    continue;
                }

                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        reader.Skip();
                        continue;
                    }

                    bool idMatches = false;
                    bool labelMatches = false;
                    bool hasValueToken = false;
                    Utf8JsonReader valueTokenSnapshot = default;

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName)
                        {
                            continue;
                        }

                        if (reader.ValueTextEquals("id"u8))
                        {
                            if (!reader.Read())
                            {
                                break;
                            }

                            if (reader.TokenType == JsonTokenType.String)
                            {
                                idMatches = JsonStringEqualsIgnoreCase(ref reader, targetField);
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                        else if (reader.ValueTextEquals("label"u8))
                        {
                            if (!reader.Read())
                            {
                                break;
                            }

                            if (reader.TokenType == JsonTokenType.String)
                            {
                                labelMatches = JsonStringEqualsIgnoreCase(ref reader, targetField);
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                        else if (reader.ValueTextEquals("value"u8))
                        {
                            if (!reader.Read())
                            {
                                break;
                            }

                            if (reader.TokenType == JsonTokenType.String)
                            {
                                valueTokenSnapshot = reader;
                                hasValueToken = true;
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                        else
                        {
                            if (!reader.Read())
                            {
                                break;
                            }

                            reader.Skip();
                        }
                    }

                    if (!foundIdField && idMatches)
                    {
                        foundIdField = true;
                        hasIdValue = hasValueToken;
                        idValueReader = valueTokenSnapshot;
                    }

                    if (!foundLabelField && labelMatches)
                    {
                        foundLabelField = true;
                        hasLabelValue = hasValueToken;
                        labelValueReader = valueTokenSnapshot;
                    }
                }
            }

            Utf8JsonReader chosenReader;
            if (foundIdField)
            {
                if (!hasIdValue)
                {
                    return CreateEmptyReadOnlySecureString();
                }

                chosenReader = idValueReader;
            }
            else if (foundLabelField)
            {
                if (!hasLabelValue)
                {
                    return CreateEmptyReadOnlySecureString();
                }

                chosenReader = labelValueReader;
            }
            else
            {
                return CreateEmptyReadOnlySecureString();
            }

            return CopyJsonTokenToSecureString(ref chosenReader);
        }

        internal static string ParsePrincipalIdentifierFromUtf8Json(
            ReadOnlySpan<byte> utf8Json,
            bool allowFallbackField)
        {
            VaultItemCategory category = ReadItemCategory(utf8Json);
            if (category == VaultItemCategory.Unknown)
            {
                return string.Empty;
            }

            var reader = new Utf8JsonReader(utf8Json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return string.Empty;
            }

            bool foundPrincipalByIdOrPurpose = false;
            bool hasPrincipalByIdOrPurposeValue = false;
            Utf8JsonReader principalByIdOrPurposeReader = default;

            bool foundPrincipalByLabel = false;
            bool hasPrincipalByLabelValue = false;
            Utf8JsonReader principalByLabelReader = default;

            bool foundCredentialById = false;
            bool hasCredentialByIdValue = false;
            Utf8JsonReader credentialByIdReader = default;

            bool foundCredentialByLabel = false;
            bool hasCredentialByLabelValue = false;
            Utf8JsonReader credentialByLabelReader = default;

            bool hasNonEmptySecretField = false;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                bool isFieldsProperty = reader.ValueTextEquals("fields"u8);
                if (!reader.Read())
                {
                    break;
                }

                if (!isFieldsProperty || reader.TokenType != JsonTokenType.StartArray)
                {
                    reader.Skip();
                    continue;
                }

                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        reader.Skip();
                        continue;
                    }

                    bool idIsPrincipal = false;
                    bool labelIsPrincipal = false;
                    bool purposeIsPrincipal = false;
                    bool idIsSecret = false;
                    bool labelIsSecret = false;
                    bool purposeIsSecret = false;
                    bool typeIsConcealed = false;
                    bool idIsCredential = false;
                    bool labelIsCredential = false;
                    bool hasValueToken = false;
                    Utf8JsonReader valueTokenSnapshot = default;

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName)
                        {
                            continue;
                        }

                        if (reader.ValueTextEquals("id"u8))
                        {
                            if (!reader.Read())
                            {
                                break;
                            }

                            if (reader.TokenType == JsonTokenType.String)
                            {
                                idIsPrincipal = JsonStringEqualsIgnoreCase(ref reader, "username")
                                    || JsonStringEqualsIgnoreCase(ref reader, "principal");
                                idIsSecret = JsonStringEqualsIgnoreCase(ref reader, "password");
                                idIsCredential = JsonStringEqualsIgnoreCase(ref reader, "credential");
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                        else if (reader.ValueTextEquals("label"u8))
                        {
                            if (!reader.Read())
                            {
                                break;
                            }

                            if (reader.TokenType == JsonTokenType.String)
                            {
                                labelIsPrincipal = JsonStringEqualsIgnoreCase(ref reader, "username")
                                    || JsonStringEqualsIgnoreCase(ref reader, "principal");
                                labelIsSecret = JsonStringEqualsIgnoreCase(ref reader, "password");
                                labelIsCredential = JsonStringEqualsIgnoreCase(ref reader, "credential");
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                        else if (reader.ValueTextEquals("purpose"u8))
                        {
                            if (!reader.Read())
                            {
                                break;
                            }

                            if (reader.TokenType == JsonTokenType.String)
                            {
                                purposeIsPrincipal = JsonStringEqualsIgnoreCase(ref reader, "USERNAME");
                                purposeIsSecret = JsonStringEqualsIgnoreCase(ref reader, "PASSWORD");
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                        else if (reader.ValueTextEquals("type"u8))
                        {
                            if (!reader.Read())
                            {
                                break;
                            }

                            if (reader.TokenType == JsonTokenType.String)
                            {
                                typeIsConcealed = JsonStringEqualsIgnoreCase(ref reader, "CONCEALED");
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                        else if (reader.ValueTextEquals("value"u8))
                        {
                            if (!reader.Read())
                            {
                                break;
                            }

                            if (reader.TokenType == JsonTokenType.String)
                            {
                                valueTokenSnapshot = reader;
                                hasValueToken = true;
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                        else
                        {
                            if (!reader.Read())
                            {
                                break;
                            }

                            reader.Skip();
                        }
                    }

                    bool isSecretField = idIsSecret || labelIsSecret || purposeIsSecret;
                    if (category == VaultItemCategory.ApiCredential
                        && allowFallbackField
                        && isSecretField
                        && hasValueToken
                        && HasUsableJsonTokenValue(ref valueTokenSnapshot))
                    {
                        hasNonEmptySecretField = true;
                    }

                    bool canBePrincipal = !isSecretField
                        && (category != VaultItemCategory.Login || !typeIsConcealed);

                    if (!foundPrincipalByIdOrPurpose && canBePrincipal && (idIsPrincipal || purposeIsPrincipal))
                    {
                        foundPrincipalByIdOrPurpose = true;
                        hasPrincipalByIdOrPurposeValue = hasValueToken;
                        principalByIdOrPurposeReader = valueTokenSnapshot;
                    }

                    if (!foundPrincipalByLabel && canBePrincipal && labelIsPrincipal)
                    {
                        foundPrincipalByLabel = true;
                        hasPrincipalByLabelValue = hasValueToken;
                        principalByLabelReader = valueTokenSnapshot;
                    }

                    if (category == VaultItemCategory.ApiCredential && allowFallbackField && !isSecretField)
                    {
                        if (!foundCredentialById && idIsCredential)
                        {
                            foundCredentialById = true;
                            hasCredentialByIdValue = hasValueToken;
                            credentialByIdReader = valueTokenSnapshot;
                        }

                        if (!foundCredentialByLabel && labelIsCredential)
                        {
                            foundCredentialByLabel = true;
                            hasCredentialByLabelValue = hasValueToken;
                            credentialByLabelReader = valueTokenSnapshot;
                        }
                    }
                }
            }

            if (foundPrincipalByIdOrPurpose)
            {
                if (hasPrincipalByIdOrPurposeValue)
                {
                    string resolved = DecodeSanitizedPrincipalToken(ref principalByIdOrPurposeReader);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        return resolved;
                    }
                }
            }
            else if (foundPrincipalByLabel && hasPrincipalByLabelValue)
            {
                string resolved = DecodeSanitizedPrincipalToken(ref principalByLabelReader);
                if (!string.IsNullOrEmpty(resolved))
                {
                    return resolved;
                }
            }

            if (category == VaultItemCategory.ApiCredential
                && allowFallbackField
                && !hasNonEmptySecretField)
            {
                if (foundCredentialById)
                {
                    return hasCredentialByIdValue
                        ? DecodeSanitizedPrincipalToken(ref credentialByIdReader)
                        : string.Empty;
                }

                if (foundCredentialByLabel && hasCredentialByLabelValue)
                {
                    return DecodeSanitizedPrincipalToken(ref credentialByLabelReader);
                }
            }

            return string.Empty;
        }

        private static VaultItemCategory ReadItemCategory(ReadOnlySpan<byte> utf8Json)
        {
            var reader = new Utf8JsonReader(utf8Json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return VaultItemCategory.Unknown;
            }

            Span<char> categoryBuffer = stackalloc char[32];
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                bool isCategory = reader.ValueTextEquals("category"u8);
                if (!reader.Read())
                {
                    return VaultItemCategory.Unknown;
                }

                if (!isCategory)
                {
                    reader.Skip();
                    continue;
                }

                if (reader.TokenType != JsonTokenType.String)
                {
                    return VaultItemCategory.Unknown;
                }

                long rawLen = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
                if (rawLen == 0 || rawLen > categoryBuffer.Length)
                {
                    return VaultItemCategory.Unknown;
                }

                int charsWritten = reader.CopyString(categoryBuffer);
                ReadOnlySpan<char> category = categoryBuffer[..charsWritten];

                if (category.Equals("LOGIN".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    return VaultItemCategory.Login;
                }

                if (category.Equals("API_CREDENTIAL".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    return VaultItemCategory.ApiCredential;
                }

                return VaultItemCategory.Unknown;
            }

            return VaultItemCategory.Unknown;
        }

        private static bool JsonStringEqualsIgnoreCase(ref Utf8JsonReader reader, string expected)
        {
            long rawLen = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
            if (rawLen < expected.Length || rawLen > expected.Length * 6L)
            {
                return false;
            }

            Span<char> buffer = stackalloc char[64];
            if (rawLen > buffer.Length)
            {
                return false;
            }

            int count = reader.CopyString(buffer);
            ReadOnlySpan<char> actual = buffer[..count];
            return actual.Equals(expected.AsSpan(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasUsableJsonTokenValue(ref Utf8JsonReader valueReader)
        {
            int maxCharLength = valueReader.HasValueSequence
                ? checked((int)valueReader.ValueSequence.Length)
                : valueReader.ValueSpan.Length;

            if (maxCharLength == 0)
            {
                return false;
            }

            char[] rentedChars = ArrayPool<char>.Shared.Rent(maxCharLength);
            try
            {
                int charCount = valueReader.CopyString(rentedChars);
                ReadOnlySpan<char> trimmed = rentedChars.AsSpan(0, charCount).Trim();
                return !trimmed.IsEmpty
                    && !trimmed.Equals(OnePasswordItem.MissingFieldSentinel.AsSpan(), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(rentedChars.AsSpan()));
                ArrayPool<char>.Shared.Return(rentedChars);
            }
        }

        private static string DecodeSanitizedPrincipalToken(ref Utf8JsonReader valueReader)
        {
            int maxCharLength = valueReader.HasValueSequence
                ? checked((int)valueReader.ValueSequence.Length)
                : valueReader.ValueSpan.Length;

            if (maxCharLength == 0)
            {
                return string.Empty;
            }

            char[] rentedChars = ArrayPool<char>.Shared.Rent(maxCharLength);
            try
            {
                int charCount = valueReader.CopyString(rentedChars);
                ReadOnlySpan<char> decoded = rentedChars.AsSpan(0, charCount);
                ReadOnlySpan<char> trimmed = decoded.Trim();

                if (trimmed.IsEmpty
                    || trimmed.Equals(OnePasswordItem.MissingFieldSentinel.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                return new string(decoded);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(rentedChars.AsSpan()));
                ArrayPool<char>.Shared.Return(rentedChars);
            }
        }

        private static SecureString CopyJsonTokenToSecureString(ref Utf8JsonReader valueReader)
        {
            int maxCharLength = valueReader.HasValueSequence
                ? checked((int)valueReader.ValueSequence.Length)
                : valueReader.ValueSpan.Length;

            if (maxCharLength == 0)
            {
                return CreateEmptyReadOnlySecureString();
            }

            char[] rentedChars = ArrayPool<char>.Shared.Rent(maxCharLength);
            try
            {
                int charCount = valueReader.CopyString(rentedChars);
                ReadOnlySpan<char> decoded = rentedChars.AsSpan(0, charCount);
                ReadOnlySpan<char> trimmed = decoded.Trim();

                if (trimmed.IsEmpty
                    || trimmed.Equals(OnePasswordItem.MissingFieldSentinel.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    return CreateEmptyReadOnlySecureString();
                }

                var secure = new SecureString();
                try
                {
                    for (int i = 0; i < charCount; i++)
                    {
                        secure.AppendChar(rentedChars[i]);
                    }

                    secure.MakeReadOnly();
                    return secure;
                }
                catch
                {
                    secure.Dispose();
                    throw;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(rentedChars.AsSpan()));
                ArrayPool<char>.Shared.Return(rentedChars);
            }
        }

        private static SecureString CreateEmptyReadOnlySecureString()
        {
            var empty = new SecureString();
            empty.MakeReadOnly();
            return empty;
        }

        public Task<string> GetPrincipalIdentifierAsync(
            string vaultId,
            string itemId,
            bool allowFallbackField = true,
            CancellationToken cancellationToken = default)
        {
            return ReadVaultItemBytesAsync(
                vaultId,
                itemId,
                allowFallbackField,
                static (utf8Json, fallback) => ParsePrincipalIdentifierFromUtf8Json(utf8Json, fallback),
                cancellationToken);
        }

        private static string SanitizeVaultValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value.Trim(), OnePasswordItem.MissingFieldSentinel, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return value;
        }

        public async Task<OnePasswordItem?> GetItemAsync(string vaultId, string itemId)
        {
            Uri uri = BuildItemUri(vaultId, itemId);

            using CancellationTokenSource? timeoutCts = CreateTimeoutCancellationTokenSource(CancellationToken.None);
            CancellationToken effectiveToken = timeoutCts?.Token ?? CancellationToken.None;

            using HttpResponseMessage response = await _httpClient
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, effectiveToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Error fetching item from 1Password: {response.StatusCode}",
                    null,
                    response.StatusCode);
            }

            using Stream stream = await response.Content
                .ReadAsStreamAsync(effectiveToken)
                .ConfigureAwait(false);

            return await JsonSerializer
                .DeserializeAsync<OnePasswordItem>(stream, cancellationToken: effectiveToken)
                .ConfigureAwait(false);
        }
    }
}