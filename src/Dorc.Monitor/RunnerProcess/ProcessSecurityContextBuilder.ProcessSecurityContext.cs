namespace Dorc.Monitor.RunnerProcess
{
    internal partial class ProcessSecurityContextBuilder
    {
        internal class ProcessSecurityContext : IDisposable
        {
            #region Disposable pattern implementation
            private bool disposedValue;
            #endregion

            public IntPtr LocallyLoggedOnUserToken { get; set; }
            public Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES ProcessAttributes;
            public Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES ThreadAttributes;

            private ProcessSecurityContext() { }

            internal ProcessSecurityContext(
                IntPtr locallyLoggedOnUserToken,
                Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES processAttributes,
                Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES threadAttributes)
            {
                if (locallyLoggedOnUserToken == IntPtr.Zero)
                {
                    throw new Exception("ProcessSecurityContext can't be created since provided locallyLoggedOnUserToken is Zero.");
                }
                this.LocallyLoggedOnUserToken = locallyLoggedOnUserToken;

                this.ProcessAttributes = processAttributes;
                this.ThreadAttributes = threadAttributes;
            }

            #region Disposable pattern implementation
            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                    }

                    Interop.Windows.Kernel32.Interop.Kernel32.CloseHandle(this.LocallyLoggedOnUserToken);

                    disposedValue = true;
                }
            }

            ~ProcessSecurityContext()
            {
                Dispose(disposing: false);
            }

            public void Dispose()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
            #endregion
        }
    }
}
