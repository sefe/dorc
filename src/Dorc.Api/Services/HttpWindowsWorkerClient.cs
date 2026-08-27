using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Dorc.Api.Exceptions;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;

namespace Dorc.Api.Services
{
    // Real implementation of IWindowsWorkerClient used on Windows installs with
    // WindowsWorker:Enabled=true. Concrete worker methods are added in later
    // S-steps (S-004 registry, S-005 WMI, S-006 password reset).
    //
    // The injected HttpClient is configured via AddHttpClient<...>() in Program.cs:
    // BaseAddress is set from WindowsWorker:Url and a WorkerKeyDelegatingHandler
    // is added so every outbound call carries the X-Worker-Key header.
    public class HttpWindowsWorkerClient : IWindowsWorkerClient
    {
        private readonly HttpClient _http;

        public HttpWindowsWorkerClient(HttpClient http)
        {
            _http = http;
        }

        public Task<ServerOperatingSystemApiModel> GetServerOperatingSystemAsync(
            string serverName,
            CancellationToken cancellationToken = default)
        {
            const string endpoint = "remote-server/operating-system";
            return SendAsync<ServerOperatingSystemApiModel>(
                endpoint,
                ct => _http.GetAsync(
                    $"{endpoint}?serverName={Uri.EscapeDataString(serverName)}",
                    ct),
                cancellationToken);
        }

        // Shared by every endpoint added in S-004/S-005/S-006 so transport failures and
        // worker error envelopes have one stable contract.
        protected async Task<T> SendAsync<T>(
            string endpoint,
            Func<CancellationToken, Task<HttpResponseMessage>> send,
            CancellationToken cancellationToken = default)
        {
            HttpResponseMessage response;
            try
            {
                response = await send(cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new WorkerUnavailableException(endpoint, ex);
            }
            catch (SocketException ex)
            {
                throw new WorkerUnavailableException(endpoint, ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new WorkerUnavailableException(endpoint, ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw new WorkerRequestRejectedException(ExtractErrorMessage(detail));
                }

                if ((int)response.StatusCode >= 500)
                {
                    throw new WorkerUnavailableException(endpoint);
                }

                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    throw new InvalidOperationException(
                        $"Windows worker returned a success status with an empty body for {endpoint}.");
                }

                var body = JsonSerializer.Deserialize<T>(
                    payload,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                return body ?? throw new InvalidOperationException(
                    $"Windows worker returned a success status with an empty body for {endpoint}.");
            }
        }

        private static string ExtractErrorMessage(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? body;
                }
            }
            catch (JsonException)
            {
                // Preserve a non-JSON worker response as the actionable error detail.
            }

            return body;
        }
    }
}
