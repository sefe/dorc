using System.Globalization;
using System.Management.Automation.Host;

namespace Dorc.PowerShell
{
    public class CustomHost : PSHost
    {
        public override string Name => "CustomHost";

        public override Version Version => new(1, 0, 0, 0);

        public override Guid InstanceId { get; } = Guid.NewGuid();

        public override PSHostUserInterface? UI => null;

        public override CultureInfo CurrentCulture { get; } = Thread.CurrentThread.CurrentCulture;

        public override CultureInfo CurrentUICulture { get; } = Thread.CurrentThread.CurrentUICulture;

        public bool ShouldExit { get; private set; }

        public int ExitCode { get; private set; }

        public override void SetShouldExit(int exitCode)
        {
            ShouldExit = true;
            ExitCode = exitCode;
        }

        public override void EnterNestedPrompt()
        {
            throw new NotSupportedException();
        }

        public override void ExitNestedPrompt()
        {
            throw new NotSupportedException();
        }

        public override void NotifyBeginApplication()
        {
        }

        public override void NotifyEndApplication()
        {
        }
    }
}
