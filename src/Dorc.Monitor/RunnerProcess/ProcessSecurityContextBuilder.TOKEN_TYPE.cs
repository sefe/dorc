namespace Dorc.Monitor.RunnerProcess
{
    internal partial class ProcessSecurityContextBuilder
    {
        /// <summary>
        /// For more details see <see cref="https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-token_type"/>
        /// </summary>
        internal enum TOKEN_TYPE
        {
            TokenPrimary = 1,
            TokenImpersonation
        }
    }
}
