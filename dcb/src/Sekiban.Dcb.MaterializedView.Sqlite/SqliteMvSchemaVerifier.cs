using Dapper;
using Microsoft.Data.Sqlite;
using Sekiban.Dcb.MaterializedView;

namespace Sekiban.Dcb.MaterializedView.Sqlite;

public sealed partial class SqliteMvRegistryStore
{
    private const string TableInfoSql =
        "SELECT name AS Name, type AS Type, \"notnull\" AS \"NotNull\", pk AS Pk, dflt_value AS DefaultSql, hidden AS Hidden FROM pragma_table_xinfo(@TableName);";

    public async Task<MvSchemaVerificationResult> VerifySchemaAsync(
        IReadOnlyList<MvSchemaTableRequirement> requirements,
        CancellationToken cancellationToken = default)
    {
        try
        {
            RecordReadOnlyConnection();
            await using var connection = new SqliteConnection(ReadOnlyConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var columns = new List<MvObservedSchemaColumn>();
            var primaryKeys = new List<MvObservedSchemaPrimaryKeyColumn>();
            var indexes = new List<MvObservedSchemaIndex>();
            foreach (var tableName in requirements.Select(requirement => requirement.PhysicalTable).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ValidateContractIdentifier(tableName);
                var objectType = await connection.ExecuteScalarAsync<string?>(
                        CatalogCommand(
                            "SELECT type FROM sqlite_master WHERE name = @TableName;",
                            new { TableName = tableName },
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
                if (!string.Equals(objectType, "table", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var tableInfo = await connection.QueryAsync<SqliteSchemaColumn>(
                        CatalogCommand(
                            TableInfoSql,
                            new { TableName = tableName },
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
                foreach (var column in tableInfo)
                {
                    columns.Add(new MvObservedSchemaColumn(
                        tableName,
                        column.Name,
                        MapType(column.Name, column.Type),
                        column.NotNull == 0)
                    {
                        DefaultSql = column.DefaultSql,
                        IsGenerated = column.Hidden is 2 or 3
                    });
                    if (column.Pk > 0)
                    {
                        primaryKeys.Add(new MvObservedSchemaPrimaryKeyColumn(
                            tableName,
                            column.Name,
                            column.Pk));
                    }
                }

                var tableIndexes = await connection.QueryAsync<SqliteSchemaIndex>(
                        CatalogCommand(
                            "SELECT name AS Name, \"unique\" AS IsUnique FROM pragma_index_list(@TableName) WHERE origin <> 'pk';",
                            new { TableName = tableName },
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
                foreach (var index in tableIndexes)
                {
                    ValidateContractIdentifier(index.Name);
                    var indexColumns = await connection.QueryAsync<SqliteSchemaIndexColumn>(
                            CatalogCommand(
                                "SELECT name AS ColumnName, seqno AS Ordinal FROM pragma_index_info(@IndexName) ORDER BY seqno;",
                                new { IndexName = index.Name },
                                cancellationToken: cancellationToken))
                        .ConfigureAwait(false);
                    indexes.Add(
                        new MvObservedSchemaIndex(
                            tableName,
                            index.Name,
                            indexColumns.OrderBy(column => column.Ordinal).Select(column => column.ColumnName).ToList(),
                            index.IsUnique != 0));
                }
            }

            return MvSchemaRequirements.Validate(
                requirements,
                MvSchemaRequirements.Observe(columns, primaryKeys, indexes));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return MvSchemaVerificationResult.Failed(
                MvInitializationFailureReason.UnsupportedProviderCapability,
                "SQLite schema metadata verification is unavailable.");
        }
    }

    private static void ValidateContractIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) ||
            !(IsAsciiLetter(identifier[0]) || identifier[0] == '_') ||
            identifier.Any(character =>
                !(IsAsciiLetter(character) || char.IsAsciiDigit(character) || character == '_')))
        {
            throw new ArgumentException(
                $"SQLite schema identifier '{identifier}' must contain only ASCII letters, digits, and underscores.",
                nameof(identifier));
        }
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static MvSchemaTypeFamily MapType(string columnName, string? declaredType)
    {
        var type = declaredType?.ToLowerInvariant() ?? string.Empty;
        var name = columnName.ToLowerInvariant();
        if (type.Contains("json", StringComparison.Ordinal)) return MvSchemaTypeFamily.Json;
        if (type.Contains("bool", StringComparison.Ordinal) || name.Contains("deleted", StringComparison.Ordinal))
            return MvSchemaTypeFamily.Boolean;
        if (type.Contains("date", StringComparison.Ordinal) || type.Contains("time", StringComparison.Ordinal))
            return MvSchemaTypeFamily.DateTime;
        if (name.Contains("date", StringComparison.Ordinal) || name.EndsWith("_at", StringComparison.Ordinal) ||
            name is "activated_at" or "last_updated" or "switched_at_utc")
            return MvSchemaTypeFamily.DateTime;
        if (type.Contains("int", StringComparison.Ordinal)) return MvSchemaTypeFamily.Integer;
        if (type.Contains("real", StringComparison.Ordinal) || type.Contains("floa", StringComparison.Ordinal) ||
            type.Contains("doub", StringComparison.Ordinal)) return MvSchemaTypeFamily.FloatingPoint;
        if (type.Contains("numeric", StringComparison.Ordinal) || type.Contains("decimal", StringComparison.Ordinal))
            return MvSchemaTypeFamily.Decimal;
        if (type.Contains("blob", StringComparison.Ordinal)) return MvSchemaTypeFamily.Binary;
        if (type.Contains("char", StringComparison.Ordinal) || type.Contains("clob", StringComparison.Ordinal) ||
            type.Contains("text", StringComparison.Ordinal)) return MvSchemaTypeFamily.String;
        return MvSchemaTypeFamily.Any;
    }

    private sealed class SqliteSchemaColumn
    {
        public string Name { get; init; } = string.Empty;
        public string? Type { get; init; }
        public int NotNull { get; init; }
        public int Pk { get; init; }
        public string? DefaultSql { get; init; }
        public int Hidden { get; init; }
    }

    private sealed class SqliteSchemaIndex
    {
        public string Name { get; init; } = string.Empty;
        public int IsUnique { get; init; }
    }

    private sealed class SqliteSchemaIndexColumn
    {
        public string ColumnName { get; init; } = string.Empty;
        public int Ordinal { get; init; }
    }
}
