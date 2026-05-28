using Dorc.Api.Interfaces;

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
    }
}
