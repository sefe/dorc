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

        dynamic view = _database.OpenView($"SELECT {select} FROM `{table}`");
        view.Execute(null);
        for (dynamic? record = view.Fetch(); record is not null; record = view.Fetch())
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

        var count = 0;
        dynamic view = _database.OpenView($"SELECT * FROM `{table}`");
        view.Execute(null);
        for (dynamic? record = view.Fetch(); record is not null; record = view.Fetch()) count++;
        return count;
    }

    private bool TableExists(string table)
    {
        dynamic view = _database.OpenView($"SELECT `Name` FROM `_Tables` WHERE `Name` = '{table}'");
        view.Execute(null);
        return view.Fetch() is not null;
    }

    public void Dispose()
    {
        // The RCWs keep the package file open, which matters because the same
        // file is read more than once in a run.
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_database);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_installer);
    }
}
