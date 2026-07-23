using Dorc.Core.Events;
using Dorc.Core.HighAvailability;
using Microsoft.Extensions.DependencyInjection;

namespace Dorc.Monitor
{
    /// <summary>
    /// DI wiring for the Monitor's Kafka-off (DB-poll) fallback mode — the
    /// else-branch of Program.cs's <c>kafkaEnabled</c> gate, extracted so the
    /// unit tests exercise the production registrations instead of a hand-copy
    /// (UC22). Registers the no-op distributed lock and the in-process poll
    /// signal; the SignalR publisher registrations are unconditional and stay
    /// in Program.cs.
    /// </summary>
    internal static class DbPollFallbackRegistration
    {
        /// <summary>
        /// Single-replica constraint: with the NoOp lock service every
        /// environment-lock acquisition short-circuits to success, so a second
        /// Monitor replica in this mode would deploy concurrently to the same
        /// environments (split brain). Nothing can detect peer replicas from
        /// here — the warning is the guard, the runbook is the control.
        /// </summary>
        internal const string SingleReplicaWarning =
            "Kafka disabled - distributed locking is OFF. " +
            "Run EXACTLY ONE Monitor replica in this mode; a second replica causes concurrent deployments to the same environment.";

        /// <summary>
        /// Registers the fallback services and emits the single-replica warning
        /// through <paramref name="warn"/>. Program.cs routes the warning to
        /// Serilog at Error level so it survives Windows-service hosting
        /// (Console.WriteLine is discarded without an attached console).
        /// </summary>
        internal static void Register(IServiceCollection services, Action<string> warn)
        {
            warn(SingleReplicaWarning);
            services.AddSingleton<IDistributedLockService, NoOpDistributedLockService>();
            services.AddSingleton<IRequestPollSignal, RequestPollSignal>();
        }
    }
}
