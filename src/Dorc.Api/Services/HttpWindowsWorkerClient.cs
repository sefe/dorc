using System.Net.Http.Json;
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
            using var resp = await _http.GetAsync(
                $"/remote-server/operating-system?serverName={Uri.EscapeDataString(serverName)}",
                cancellationToken);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<ServerOperatingSystemApiModel>(cancellationToken: cancellationToken);
            return body ?? throw new WorkerUnavailableException("remote-server/operating-system");
        }
    }
}
