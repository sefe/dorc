namespace Dorc.Monitor.RunnerProcess
{
    internal partial class ProcessSecurityContextBuilder
    {
        /// <summary>
        /// For more details see <see cref="https://learn.microsoft.com/en-us/windows/win32/secauthn/logonuserexexw"/>
        /// </summary>
        internal enum LOGON_PROVIDER
        {
            LOGON32_PROVIDER_DEFAULT,
            LOGON32_PROVIDER_WINNT35,
            LOGON32_PROVIDER_WINNT40,
            LOGON32_PROVIDER_WINNT50
        }
    }
}
