using System;
using System.Globalization;
using System.Management.Automation.Host;
using System.Threading;

namespace Dorc.NetFramework.PowerShell
{
    public class CustomHost : PSHost
    {
        public CustomHostUserInterface HostUserInterface { get; }

        public CustomHost()
        {
            HostUserInterface = new CustomHostUserInterface();
        }

        public override string Name => "CustomHost";

        public override Version Version => new Version(1, 0, 0, 0);

        public override Guid InstanceId { get; } = Guid.NewGuid();

        public override PSHostUserInterface UI => HostUserInterface;

        public override CultureInfo CurrentCulture { get; } = Thread.CurrentThread.CurrentCulture;

        public override CultureInfo CurrentUICulture { get; } = Thread.CurrentThread.CurrentUICulture;

        public override void SetShouldExit(int exitCode)
        {
        }

        public override void EnterNestedPrompt()
        {
            throw new NotImplementedException();
        }

        public override void ExitNestedPrompt()
        {
            throw new NotImplementedException();
        }

        public override void NotifyBeginApplication()
        {
        }

        public override void NotifyEndApplication()
        {
        }
    }
}