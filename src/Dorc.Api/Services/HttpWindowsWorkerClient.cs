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

        public async Task<ServerOperatingSystemApiModel> GetServerOperatingSystemAsync(string serverName, CancellationToken cancellationToken = default)
        {
            const string endpoint = "remote-server/operating-system";

            HttpResponseMessage resp;
            try
            {
                resp = await _http.GetAsync(
                    $"{endpoint}?serverName={Uri.EscapeDataString(serverName)}",
                    cancellationToken);
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
                    var detail = await resp.Content.ReadAsStringAsync(cancellationToken);
                    throw new WorkerRequestRejectedException(detail);
                }

                // 5xx from the worker means the worker itself is faulted — still unavailable
                // from the caller's point of view.
                if ((int)resp.StatusCode >= 500)
                {
                    throw new WorkerUnavailableException(endpoint);
                }

                resp.EnsureSuccessStatusCode();

                var body = await resp.Content.ReadFromJsonAsync<ServerOperatingSystemApiModel>(cancellationToken: cancellationToken);

                // A 200 with an empty body is a worker bug, NOT unavailability. Reporting it
                // as 503 sends operators hunting for a dead process that is running fine.
                return body ?? throw new InvalidOperationException(
                    $"Windows worker returned a success status with an empty body for {endpoint}.");
            }
        }
    }
}
