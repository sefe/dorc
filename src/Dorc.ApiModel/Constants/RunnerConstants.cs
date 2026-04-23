using System.Runtime.InteropServices;

namespace Dorc.ApiModel.Constants
{
    public static class RunnerConstants
    {
        public const string StandardStreamEndString = "[*** STANDARD STREAM END ***]";

        public static string ScriptGroupFilesPath
        {
            get
            {
                var envPath = Environment.GetEnvironmentVariable("DORC_SCRIPTGROUP_FILES_PATH");
                if (!string.IsNullOrEmpty(envPath))
                    return envPath;

                return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? @"c:\Log\DOrc\Deploy\Services\ScriptGroupsPipeFiles\"
                    : "/var/log/dorc/scriptgroup-files/";
            }
        }
    }
}
