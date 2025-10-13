using System;

namespace Dorc.ApiModel
{
    public enum PowerShellVersion
    {
        V5_1,
        V7
    }

    public static class PowerShellVersionExtensions
    {
        public static string ToVersionString(this PowerShellVersion version)
        {
            switch (version)
            {
                case PowerShellVersion.V5_1:
                    return "v5.1";
                case PowerShellVersion.V7:
                    return "v7";
                default:
                    throw new ArgumentOutOfRangeException(nameof(version), version, null);
            }
        }

        public static PowerShellVersion FromVersionString(string versionString)
        {
            switch (versionString)
            {
                case "v5.1":
                    return PowerShellVersion.V5_1;
                case "v7":
                    return PowerShellVersion.V7;
                default:
                    throw new ArgumentException($"Unknown PowerShell version: {versionString}", nameof(versionString));
            }
        }

        public static PowerShellVersion? TryFromVersionString(string versionString)
        {
            if (string.IsNullOrEmpty(versionString))
                return null;

            switch (versionString)
            {
                case "v5.1":
                    return PowerShellVersion.V5_1;
                case "v7":
                    return PowerShellVersion.V7;
                default:
                    return null;
            }
        }
    }
}
