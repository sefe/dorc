namespace Dorc.Monitor.Terraform
{
    // Serialises plan/apply on the same (environment, component) key so two
    // simultaneous DOrc deployment attempts cannot race the same Terraform
    // state. In-process only: terraform's azurerm backend handles
    // cross-process state locking via blob lease; this guard surfaces a clean
    // operator-visible error when DOrc itself sees concurrent operations
    // before terraform is invoked.
    public sealed class TerraformConcurrencyGuard
    {
        // Process-wide map of (env|component) -> semaphore. SemaphoreSlim with
        // initial = 1 enforces single-flight; the guard records the acquiring
        // operation's correlation token so a denied attempt can report what
        // is currently running.
        private static readonly Dictionary<string, KeyedSlot> Slots = new();
        private static readonly object SlotsLock = new();

        public static TerraformConcurrencyGuard Instance { get; } = new();

        // Default - bounded so a stuck runner does not block a new attempt
        // forever. Caller may override.
        public TimeSpan DefaultAcquisitionTimeout { get; init; } = TimeSpan.FromMinutes(2);

        internal int RetainedKeyCount
        {
            get { lock (SlotsLock) return Slots.Count; }
        }

        public IDisposable Acquire(string project, string environment, string component, string operationCorrelationId,
            CancellationToken cancellationToken = default)
            => Acquire(project, environment, component, operationCorrelationId, DefaultAcquisitionTimeout, cancellationToken);

        public IDisposable Acquire(string project, string environment, string component, string operationCorrelationId,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(environment)) throw new ArgumentException("environment required", nameof(environment));
            if (string.IsNullOrEmpty(component)) throw new ArgumentException("component required", nameof(component));
            cancellationToken.ThrowIfCancellationRequested();

            var key = MakeKey(project, environment, component);
            var slot = ReferenceSlot(key);
            try
            {
                if (!slot.Semaphore.Wait(timeout, cancellationToken))
                    throw new TerraformConcurrentOperationException(
                        environment, component, Volatile.Read(ref slot.CurrentOperationId), timeout);
                Volatile.Write(ref slot.CurrentOperationId, operationCorrelationId);
                return new Release(key, slot);
            }
            catch
            {
                ReleaseReference(key, slot);
                throw;
            }
        }

        private static KeyedSlot ReferenceSlot(string key)
        {
            lock (SlotsLock)
            {
                if (!Slots.TryGetValue(key, out var slot))
                    Slots.Add(key, slot = new KeyedSlot());
                slot.References++;
                return slot;
            }
        }

        private static void ReleaseReference(string key, KeyedSlot slot)
        {
            lock (SlotsLock)
            {
                // References include waiters, so retiring a slot cannot split
                // one state identity across two live semaphores.
                if (--slot.References != 0) return;
                Slots.Remove(key);
                slot.Semaphore.Dispose();
            }
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
            public string? CurrentOperationId;
            public int References;
        }

        private sealed class Release : IDisposable
        {
            private readonly string key;
            private KeyedSlot? slot;

            public Release(string key, KeyedSlot slot) { this.key = key; this.slot = slot; }

            public void Dispose()
            {
                var owned = Interlocked.Exchange(ref slot, null);
                if (owned is null) return;
                Volatile.Write(ref owned.CurrentOperationId, null);
                owned.Semaphore.Release();
                ReleaseReference(key, owned);
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
