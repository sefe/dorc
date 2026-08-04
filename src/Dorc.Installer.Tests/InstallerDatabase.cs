using System.Runtime.Versioning;

namespace Dorc.Installer.Tests;

/// <summary>
/// Reads the tables of an installer package through the WindowsInstaller COM
/// API.
///
/// This replaced a PowerShell implementation of the same comparison. Every
/// failure that one had — fields arriving as <c>System.String[]</c>, rows
/// collapsing to a single key, a one-element result becoming a bare string,
/// whole tables silently reading as empty — came from the same property of the
/// language: collections change shape as they cross function and pipeline
/// boundaries, and the failures are silent. None of them are expressible here.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class InstallerDatabase : IDisposable
{
    private const int ReadOnly = 0;

    private readonly dynamic _installer;
    private readonly dynamic _database;

    public InstallerDatabase(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Installer package not found", path);

        var type = Type.GetTypeFromProgID("WindowsInstaller.Installer")
                   ?? throw new InvalidOperationException("WindowsInstaller.Installer is not registered on this machine.");
        _installer = Activator.CreateInstance(type)
                     ?? throw new InvalidOperationException("Could not create WindowsInstaller.Installer.");
        _database = _installer.OpenDatabase(path, ReadOnly);
        Path = path;
    }

    public string Path { get; }

    /// <summary>
    /// Rows of the named table, each row holding exactly the columns asked
    /// for, in the order asked for. Absent tables yield an empty sequence —
    /// not every package installs services or certificates.
    /// </summary>
    /// <remarks>
    /// The columns are named in the query rather than discovered from the
    /// view. Asking the view for its column names is what the first version
    /// did, and it failed on the build agent with a type mismatch on the field
    /// index; naming them removes the call rather than working around it.
    /// </remarks>
    public IReadOnlyList<string[]> Read(string table, params string[] columns)
    {
        if (columns.Length == 0) throw new ArgumentException("Name the columns to read.", nameof(columns));
        if (!TableExists(table)) return Array.Empty<string[]>();

        var select = string.Join(", ", columns.Select(c => $"`{c}`"));
        var rows = new List<string[]>();

        foreach (dynamic record in Fetch($"SELECT {select} FROM `{table}`"))
        {
            var row = new string[columns.Length];
            for (var i = 0; i < columns.Length; i++)
            {
                // StringData is 1-based, and returns null for a NULL column.
                row[i] = (string?)record.StringData[i + 1] ?? string.Empty;
            }
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Row count only, for tables reported rather than compared.</summary>
    public int CountRows(string table)
    {
        if (!TableExists(table)) return 0;

        return Fetch($"SELECT * FROM `{table}`").Count();
    }

    private bool TableExists(string table)
        => Fetch($"SELECT `Name` FROM `_Tables` WHERE `Name` = '{table}'").Any();

    /// <summary>
    /// Rows of a query, with the view and every record released as soon as it
    /// has been read. The RCWs hold the package file open otherwise, and the
    /// same file is read several times in a run — once per test, and twice in
    /// the self-test — so leaving them to finalisation risks a file lock that
    /// would show up as an unrelated flake.
    /// </summary>
    private IEnumerable<object> Fetch(string sql)
    {
        dynamic view = _database.OpenView(sql);
        try
        {
            view.Execute(null);
            while (true)
            {
                dynamic? record = view.Fetch();
                if (record is null) yield break;
                try { yield return record; }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(record); }
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(view);
        }
    }

    public void Dispose()
    {
        // The RCWs keep the package file open, which matters because the same
        // file is read more than once in a run.
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_database);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_installer);
    }
}
