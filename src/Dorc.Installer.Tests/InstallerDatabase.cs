using System.Runtime.Versioning;
using WixToolset.Dtf.WindowsInstaller;

namespace Dorc.Installer.Tests;

/// <summary>
/// Reads the tables of an installer package.
///
/// This is the WiX toolset's own managed wrapper over msi.dll. Reading the
/// same five packages through the WindowsInstaller COM API took 65 seconds of
/// every build: <c>dynamic</c> binds late, so fetching a row cost an IDispatch
/// round-trip per cell, and the baseline package alone has around two thousand
/// file rows. Here a whole table crosses the boundary in one call.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class InstallerDatabase : IDisposable
{
    private readonly Database _database;

    public InstallerDatabase(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Installer package not found", path);

        _database = new Database(path, DatabaseOpenMode.ReadOnly);
    }

    /// <summary>
    /// Rows of the named table, each row holding exactly the columns asked
    /// for, in the order asked for. Absent tables yield an empty sequence —
    /// not every package installs services or certificates.
    /// </summary>
    public IReadOnlyList<string[]> Read(string table, params string[] columns)
    {
        if (columns.Length == 0) throw new ArgumentException("Name the columns to read.", nameof(columns));
        if (!_database.Tables.Contains(table)) return Array.Empty<string[]>();

        var select = string.Join(", ", columns.Select(c => $"`{c}`"));

        // One call per table, returning every field row-major. The row width is
        // the number of columns named above, so the flat list re-splits exactly.
        var fields = _database.ExecuteStringQuery($"SELECT {select} FROM `{table}`");

        var rows = new List<string[]>(fields.Count / columns.Length);
        for (var offset = 0; offset + columns.Length <= fields.Count; offset += columns.Length)
        {
            var row = new string[columns.Length];
            for (var column = 0; column < columns.Length; column++)
                row[column] = fields[offset + column] ?? string.Empty;
            rows.Add(row);
        }
        return rows;
    }

    public void Dispose() => _database.Dispose();
}
