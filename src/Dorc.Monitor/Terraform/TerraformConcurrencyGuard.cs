using System.Collections.Concurrent;

namespace Dorc.Monitor.Terraform
{
    // Serialises plan/apply on the same (environment, component) key so two
    // simultaneous DOrc deployment attempts cannot race the same Terraform
    // state. In-process only: terraform's azurerm backend handles
    // cross-process state locking via blob lease; this guard surfaces a clean
    // operator-visible error when DOrc itself sees concurrent operations
    // before terraform is invoked.
    //
    // partial. Cross-monitor explicit blob-lease acquisition is a
    // follow-up under the lifecycle owner.
    public sealed class TerraformConcurrencyGuard
    {
        // Process-wide map of (env|component) -> semaphore. SemaphoreSlim with
        // initial = 1 enforces single-flight; the guard records the acquiring
        // operation's correlation token so a denied attempt can report what
        // is currently running.
        private static readonly ConcurrentDictionary<string, KeyedSlot> Slots = new();

        public static TerraformConcurrencyGuard Instance { get; } = new();

        // Default - bounded so a stuck runner does not block a new attempt
        // forever. Caller may override.
        public TimeSpan DefaultAcquisitionTimeout { get; init; } = TimeSpan.FromMinutes(2);

        public IDisposable Acquire(string project, string environment, string component, string operationCorrelationId)
            => Acquire(project, environment, component, operationCorrelationId, DefaultAcquisitionTimeout);

        public IDisposable Acquire(string project, string environment, string component, string operationCorrelationId, TimeSpan timeout)
        {
            if (string.IsNullOrEmpty(environment)) throw new ArgumentException("environment required", nameof(environment));
            if (string.IsNullOrEmpty(component)) throw new ArgumentException("component required", nameof(component));

            var key = MakeKey(project, environment, component);
            var slot = Slots.GetOrAdd(key, _ => new KeyedSlot());

            if (!slot.Semaphore.Wait(timeout))
            {
                throw new TerraformConcurrentOperationException(
                    environment,
                    component,
                    slot.CurrentOperationId,
                    timeout);
            }
            slot.CurrentOperationId = operationCorrelationId;
            return new Release(slot);
        }

        // Uses the same normalization and the same (project, environment,
        // component) triple as the blob state key, so the guard serializes
        // exactly the set of operations that would contend on one tfstate
        // (e.g. "Prod EU" and "Prod-EU" are distinct states and may run
        // concurrently; "PROD" and "prod" are the same state and are
        // serialized; the same component + environment under two different
        // projects are distinct states).
        private static string MakeKey(string project, string environment, string component)
            => TerraformStateKeySanitizer.Sanitize(project) + "|" +
               TerraformStateKeySanitizer.Sanitize(environment) + "|" +
               TerraformStateKeySanitizer.Sanitize(component);

        private sealed class KeyedSlot
        {
            public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(1, 1);
            public string? CurrentOperationId { get; set; }
        }

        private sealed class Release : IDisposable
        {
            private readonly KeyedSlot slot;
            private bool disposed;

            public Release(KeyedSlot slot) { this.slot = slot; }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                slot.CurrentOperationId = null;
                slot.Semaphore.Release();
            }
        }
    }

    public sealed class TerraformConcurrentOperationException : InvalidOperationException
    {
        public string Environment { get; }
        public string Component { get; }
        public string? ContendingOperationId { get; }

        public TerraformConcurrentOperationException(string environment, string component, string? contendingOperationId, TimeSpan waited)
            : base($"Another Terraform operation is already in flight on environment '{environment}' / component '{component}'" +
                   (contendingOperationId is null ? "" : $" (correlation '{contendingOperationId}')") +
                   $". Waited {waited.TotalSeconds}s for it to complete; refusing to start a concurrent run.")
        {
            Environment = environment;
            Component = component;
            ContendingOperationId = contendingOperationId;
        }
    }
}
