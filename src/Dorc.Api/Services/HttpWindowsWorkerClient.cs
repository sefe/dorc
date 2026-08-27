using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Dorc.Api.Exceptions;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;

namespace Dorc.Api.Services
{
    // Real implementation of IWindowsWorkerClient used on Windows installs with
    // WindowsWorker:Enabled=true. The injected HttpClient is configured via
    // AddHttpClient<...>() in Program.cs (BaseAddress from WindowsWorker:Url +
    // WorkerKeyDelegatingHandler attaching X-Worker-Key).
    public class HttpWindowsWorkerClient : IWindowsWorkerClient
    {
        private readonly HttpClient _http;

        public HttpWindowsWorkerClient(HttpClient http)
        {
            _http = http;
        }

        public Task<ServerOperatingSystemApiModel> GetServerOperatingSystemAsync(string serverName, CancellationToken cancellationToken = default)
        {
            const string endpoint = "remote-server/operating-system";
            return SendAsync<ServerOperatingSystemApiModel>(
                endpoint,
                ct => _http.GetAsync($"{endpoint}?serverName={Uri.EscapeDataString(serverName)}", ct),
                cancellationToken);
        }

        public Task<List<WorkerDaemonApiModel>> ProbeDaemonStatusesAsync(WorkerDaemonProbeRequestApiModel request, CancellationToken cancellationToken = default)
        {
            const string endpoint = "daemons/probe";
            return SendAsync<List<WorkerDaemonApiModel>>(
                endpoint,
                ct => System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(_http, endpoint, request, ct),
                cancellationToken);
        }

        public Task<WorkerDaemonApiModel> ChangeDaemonStateAsync(WorkerDaemonStateChangeRequestApiModel request, CancellationToken cancellationToken = default)
        {
            const string endpoint = "daemons/change-state";
            return SendAsync<WorkerDaemonApiModel>(
                endpoint,
                ct => System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(_http, endpoint, request, ct),
                cancellationToken);
        }

        public Task RebootServerAsync(string serverName, CancellationToken cancellationToken = default)
        {
            const string endpoint = "remote-server/reboot";
            return SendAsync<ApiBoolResult>(
                endpoint,
                ct => _http.PostAsync($"{endpoint}?serverName={Uri.EscapeDataString(serverName)}", content: null, ct),
                cancellationToken);
        }

        public Task<ApiBoolResult> ResetAppPasswordAsync(WorkerPasswordResetRequestApiModel request, CancellationToken cancellationToken = default)
        {
            const string endpoint = "password-reset";
            return SendAsync<ApiBoolResult>(
                endpoint,
                ct => System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(_http, endpoint, request, ct),
                cancellationToken);
        }

        // One send pipeline for every endpoint, so unavailability mapping, the worker's 400
        // rejection contract, and empty-body detection cannot drift between operations.
        private async Task<T> SendAsync<T>(string endpoint, Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
        {
            HttpResponseMessage resp;
            try
            {
                resp = await send(cancellationToken);
            }
            // A worker that is configured but not running (connection refused, process
            // crashed, host unreachable, request timed out) is "unavailable" — the same
            // condition WorkerUnavailableClient represents. Without this it surfaced as an
            // unhandled HttpRequestException → 500, so the documented 503 only ever fired
            // for the config-disabled case, never for real runtime unavailability.
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
                // HttpClient timeout, not a caller-initiated cancellation.
                throw new WorkerUnavailableException(endpoint, ex);
            }

            using (resp)
            {
                // The worker answers 400 for genuine user-facing failures ("cannot open the
                // target machine"). Surface that as a 400 rather than collapsing it into a
                // 500, which is what EnsureSuccessStatusCode did.
                if (resp.StatusCode == HttpStatusCode.BadRequest)
                {
                    // The worker's 400 body is already {"error":"..."}. Reading it whole and
                    // re-wrapping in the filter produced {"error":"{\"error\":\"...\"}"} —
                    // unwrap to the message so the client sees a single, flat envelope.
                    var detail = await resp.Content.ReadAsStringAsync(cancellationToken);
                    throw new WorkerRequestRejectedException(ExtractErrorMessage(detail));
                }

                // 5xx from the worker means the worker itself is faulted — still unavailable
                // from the caller's point of view.
                if ((int)resp.StatusCode >= 500)
                {
                    throw new WorkerUnavailableException(endpoint);
                }

                resp.EnsureSuccessStatusCode();

                var body = await resp.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);

                // A 200 with an empty body is a worker bug, NOT unavailability. Reporting it
                // as 503 sends operators hunting for a dead process that is running fine.
                return body ?? throw new InvalidOperationException(
                    $"Windows worker returned a success status with an empty body for {endpoint}.");
            }
        }

        // Pulls the "error" property out of the worker's {"error":"..."} body. Falls back to
        // the raw body when it is not that shape, so an unexpected payload is not swallowed.
        private static string ExtractErrorMessage(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("error", out var err)
                    && err.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return err.GetString() ?? body;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Not JSON — fall through and surface the raw body.
            }
            return body;
        }
    }
}
