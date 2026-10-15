using Microsoft.Win32.SafeHandles;
using System.Runtime.Versioning;

namespace Dorc.Monitor.RunnerProcess
{
    [SupportedOSPlatform("windows")]
    internal partial class ProcessSecurityContextBuilder
    {
        /// <summary>
        /// Everything CreateProcessAsUser needs to start a Runner as the deployment account,
        /// and everything that has to be released afterwards.
        ///
        /// The releasing is the point. This previously closed one handle — the primary token —
        /// while the logon token it was derived from and the unmanaged security descriptor
        /// were both dropped on the floor, once per script group, for the life of the service.
        /// </summary>
        internal class ProcessSecurityContext : IDisposable
        {
            #region Disposable pattern implementation
            private bool disposedValue;
            #endregion

            private readonly SafeAccessTokenHandle logonToken;
            private readonly SafeAccessTokenHandle locallyLoggedOnUserToken;
            private readonly RunnerProcessSecurityDescriptor securityDescriptor;

            public IntPtr LocallyLoggedOnUserToken =>
                this.locallyLoggedOnUserToken.DangerousGetHandle();
            public Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES ProcessAttributes;
            public Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES ThreadAttributes;

            private ProcessSecurityContext() { }

            internal ProcessSecurityContext(
                SafeAccessTokenHandle locallyLoggedOnUserToken,
                SafeAccessTokenHandle logonToken,
                RunnerProcessSecurityDescriptor securityDescriptor,
                Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES processAttributes,
                Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES threadAttributes)
            {
                if (locallyLoggedOnUserToken.IsInvalid)
                {
                    throw new Exception("ProcessSecurityContext can't be created since provided locallyLoggedOnUserToken is Zero.");
                }
                this.locallyLoggedOnUserToken = locallyLoggedOnUserToken;
                this.logonToken = logonToken;
                this.securityDescriptor = securityDescriptor;

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

                    this.locallyLoggedOnUserToken.Dispose();
                    this.logonToken.Dispose();

                    // Unmanaged memory, so it is released on the finalizer path too.
                    this.securityDescriptor?.Dispose();

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
