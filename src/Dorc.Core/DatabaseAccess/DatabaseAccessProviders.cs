namespace Dorc.Core.DatabaseAccess;

public sealed class DatabaseAccessProviders(IEnumerable<IDatabaseAccessProvider> providers)
{
    private readonly IReadOnlyDictionary<string, IDatabaseAccessProvider> registered =
        providers.ToDictionary(p => p.Name, StringComparer.Ordinal);

    public IDatabaseAccessProvider Resolve(string name) =>
        name != null && registered.TryGetValue(name, out var provider)
            ? provider
            : throw new NotSupportedException($"Unsupported database access provider '{name}'.");

    public string[] Names => registered.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
}
