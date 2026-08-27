using Dorc.ApiModel;
using Microsoft.Win32;

namespace Dorc.Api.WindowsWorker.Services
{
    public class RemoteRegistryOperatingSystemReader : IRemoteServerOperatingSystemReader
    {
        public ServerOperatingSystemApiModel? Read(string serverName)
        {
            using var registry = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, serverName);
            using var key = registry.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\");
            return key == null
                ? null
                : MapValues(
                    key.GetValue("ProductName")?.ToString(),
                    key.GetValue("CurrentVersion")?.ToString());
        }

        internal static ServerOperatingSystemApiModel MapValues(
            string? productName,
            string? currentVersion)
            => new()
            {
                ProductName = productName ?? string.Empty,
                CurrentVersion = currentVersion ?? string.Empty
            };
    }
}
