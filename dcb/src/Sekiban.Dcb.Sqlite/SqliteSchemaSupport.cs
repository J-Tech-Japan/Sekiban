using Microsoft.Data.Sqlite;
namespace Sekiban.Dcb.Sqlite;

/// <summary>
///     The ONE place the SQLite stores probe schema state. Extracted so the byte-identical <c>TableExists</c> /
///     <c>HasColumn</c> / identifier-allowlist checks live once instead of being copied into both
///     <c>SqliteEventStore</c> and <c>SqliteMultiProjectionStateStore</c>. The exact SQL strings are preserved verbatim
///     (golden-pinned by <c>SqliteSchemaSupportGoldenTests</c>) so this is a pure de-duplication with no behavior change.
/// </summary>
internal static class SqliteSchemaSupport
{
    internal const string TableExistsSql = "SELECT name FROM sqlite_master WHERE type='table' AND name = @name";
    internal const string ColumnsSql = "SELECT name FROM pragma_table_info(@tableName);";

    public static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = TableExistsSql;
        cmd.Parameters.AddWithValue("@name", tableName);
        var result = cmd.ExecuteScalar();
        return result != null && result != DBNull.Value;
    }

    public static bool HasColumn(
        SqliteConnection connection,
        string tableName,
        string columnName,
        IReadOnlySet<string> allowedTables,
        IReadOnlySet<string> allowedColumns)
    {
        ValidateSchemaIdentifier(tableName, columnName, allowedTables, allowedColumns);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = ColumnsSql;
        cmd.Parameters.AddWithValue("@tableName", tableName);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(0), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // A column name is only ever concatenated into DDL after passing this allowlist, so a pragma-driven ADD COLUMN can
    // never inject arbitrary SQL — the identifier is bounded to a known, per-store set.
    public static void ValidateSchemaIdentifier(
        string tableName,
        string columnName,
        IReadOnlySet<string> allowedTables,
        IReadOnlySet<string> allowedColumns)
    {
        if (!allowedTables.Contains(tableName) || !allowedColumns.Contains(columnName))
        {
            throw new ArgumentException($"Unsupported schema identifier: {tableName}.{columnName}");
        }
    }
}
