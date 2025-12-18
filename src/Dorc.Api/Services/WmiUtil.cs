using System.Management;
using System.Runtime.Versioning;

namespace Dorc.Api.Services
{
    [SupportedOSPlatform("windows")]
    public class WmiUtil
    {
        private string _path = null!;
        private ManagementScope _scope = null!;
        private string _serverName = null!;

        public WmiUtil(string server)
        {
            _serverName = server;
            Init(_serverName);
        }

        public WmiUtil()
        {
        }

        public bool IsConnected
        {
            get
            {
                if (_scope != null)
                    return _scope.IsConnected;
                return false;
            }
        }

        /// <summary>
        ///     Reboots remote server using wmi call
        /// </summary>
        /// <param name="server">Server name to reboot</param>
        public void Reboot()
        {
            if (IsConnected)
                try
                {
                    var moclass = @"Win32_OperatingSystem";
                    var path = $"{_path}:{moclass}";
                    var mp = new ManagementPath(path);
                    var mo = new ManagementObject(_scope, mp, null);
                    var outResult = mo.InvokeMethod("Reboot", null, null);
                }
                catch (ManagementException err)
                {
                    throw new Exception("An error occurred while trying to execute the WMI method: " + err.Message);
                }
        }

        public string GetComputerName()
        {
            var name = "";
            try
            {
                var query = new ObjectQuery("SELECT * FROM Win32_OperatingSystem");
                var searcher =
                    new ManagementObjectSearcher(_scope, query);
                foreach (ManagementObject queryObj in searcher.Get())
                {
                    Console.WriteLine("-----------------------------------");
                    Console.WriteLine("Win32_OperatingSystem instance");
                    Console.WriteLine("-----------------------------------");
                    Console.WriteLine("CSName: {0}", queryObj["CSName"]);
                    name = queryObj["CSName"].ToString();
                }
            }
            catch (ManagementException)
            {
                return null;
            }

            return name;
        }

        public ulong GetMemory()
        {
            try
            {
                var query = new ObjectQuery("SELECT Capacity FROM Win32_PhysicalMemory");
                var searcher = new ManagementObjectSearcher(_scope, query);
                ulong capacity = 0;
                foreach (ManagementObject mo in searcher.Get()) capacity += (ulong)mo["Capacity"];
                return capacity;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public bool Init(string serverName)
        {
            try
            {
                _serverName = serverName;
                _path = $@"\\{_serverName}\root\CIMV2";
                _scope = new ManagementScope(_path, new ConnectionOptions
                {
                    EnablePrivileges = true,
                    Authentication = AuthenticationLevel.Default,
                    Impersonation = ImpersonationLevel.Impersonate
                });
                _scope.Connect();
            }
            catch
            {
                return false;
            }

            return _scope.IsConnected;
        }
    }
}