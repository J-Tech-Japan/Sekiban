using Dapper;
using Npgsql;
using Sekiban.Dcb.MaterializedView;

namespace Sekiban.Dcb.MaterializedView.Postgres;

public sealed partial class PostgresMvRegistryStore
{
    public async Task<MvSchemaVerificationResult> VerifySchemaAsync(
        IReadOnlyList<MvSchemaTableRequirement> requirements,
        CancellationToken cancellationToken = default)
    {
        try
        {
            RecordReadOnlyConnection();
            await using var connection = new NpgsqlConnection(ReadOnlyConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var tableNames = requirements
                .Select(requirement => requirement.PhysicalTable)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var columns = await connection.QueryAsync<PostgresSchemaColumn>(
                    CatalogCommand(
                        """
                        SELECT table_name AS TableName,
                               column_name AS ColumnName,
                               data_type AS DataType,
                               udt_name AS UdtName,
                               is_nullable AS IsNullable,
                               column_default AS DefaultSql,
                               is_generated AS IsGenerated,
                               generation_expression AS GenerationExpression,
                               character_maximum_length AS MaxLength,
                               numeric_precision AS Precision,
                               numeric_scale AS Scale
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = ANY(@TableNames);
                        """,
                        new { TableNames = tableNames },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            var primaryKeys = await connection.QueryAsync<PostgresSchemaPrimaryKeyColumn>(
                    CatalogCommand(
                        """
                        SELECT tc.table_name AS TableName,
                               kcu.column_name AS ColumnName,
                               kcu.ordinal_position AS Ordinal
                        FROM information_schema.table_constraints AS tc
                        INNER JOIN information_schema.key_column_usage AS kcu
                            ON tc.constraint_name = kcu.constraint_name
                           AND tc.table_schema = kcu.table_schema
                           AND tc.table_name = kcu.table_name
                        WHERE tc.table_schema = current_schema()
                          AND tc.constraint_type = 'PRIMARY KEY'
                          AND tc.table_name = ANY(@TableNames);
                        """,
                        new { TableNames = tableNames },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            var indexes = await connection.QueryAsync<PostgresSchemaIndex>(
                    CatalogCommand(
                        """
                        SELECT tablename AS TableName,
                               indexname AS Name,
                               indexdef LIKE '%UNIQUE INDEX%' AS IsUnique,
                               regexp_replace(indexdef, '^.*\\((.*)\\).*$', '\\1') AS ColumnList
                        FROM pg_indexes
                        WHERE schemaname = current_schema()
                          AND tablename = ANY(@TableNames)
                          AND indexdef NOT LIKE '%PRIMARY KEY%';
                        """,
                        new { TableNames = tableNames },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            return MvSchemaRequirements.Validate(
                requirements,
                MvSchemaRequirements.Observe(
                    columns.Select(column => new MvObservedSchemaColumn(
                        column.TableName,
                        column.ColumnName,
                        MapType(column.DataType, column.UdtName),
                        string.Equals(column.IsNullable, "YES", StringComparison.OrdinalIgnoreCase))
                    {
                        DefaultSql = column.DefaultSql,
                        IsGenerated = string.Equals(column.IsGenerated, "ALWAYS", StringComparison.OrdinalIgnoreCase),
                        GenerationExpression = column.GenerationExpression,
                        MaxLength = column.MaxLength,
                        Precision = column.Precision,
                        Scale = column.Scale
                    }),
                    primaryKeys.Select(column => new MvObservedSchemaPrimaryKeyColumn(
                        column.TableName,
                        column.ColumnName,
                        column.Ordinal)),
                    indexes.Select(index => new MvObservedSchemaIndex(
                        index.TableName,
                        index.Name,
                        index.ColumnList
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(column => column.Trim('"'))
                            .ToList(),
                        index.IsUnique))));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return MvSchemaVerificationResult.Failed(
                MvInitializationFailureReason.UnsupportedProviderCapability,
                "PostgreSQL schema metadata verification is unavailable.");
        }
    }

    private static MvSchemaTypeFamily MapType(string dataType, string udtName)
    {
        var type = $"{dataType} {udtName}".ToLowerInvariant();
        if (type.Contains("json", StringComparison.Ordinal)) return MvSchemaTypeFamily.Json;
        if (type.Contains("uuid", StringComparison.Ordinal)) return MvSchemaTypeFamily.Guid;
        if (type.Contains("bool", StringComparison.Ordinal)) return MvSchemaTypeFamily.Boolean;
        if (type.Contains("timestamp", StringComparison.Ordinal) ||
            type.Contains("date", StringComparison.Ordinal) ||
            type.Contains("time", StringComparison.Ordinal)) return MvSchemaTypeFamily.DateTime;
        if (type.Contains("int", StringComparison.Ordinal) || type.Contains("serial", StringComparison.Ordinal))
            return MvSchemaTypeFamily.Integer;
        if (type.Contains("numeric", StringComparison.Ordinal) || type.Contains("decimal", StringComparison.Ordinal))
            return MvSchemaTypeFamily.Decimal;
        if (type.Contains("double", StringComparison.Ordinal) || type.Contains("real", StringComparison.Ordinal))
            return MvSchemaTypeFamily.FloatingPoint;
        if (type.Contains("bytea", StringComparison.Ordinal)) return MvSchemaTypeFamily.Binary;
        if (type.Contains("char", StringComparison.Ordinal) || type.Contains("text", StringComparison.Ordinal))
            return MvSchemaTypeFamily.String;
        return MvSchemaTypeFamily.Any;
    }

    private sealed class PostgresSchemaColumn
    {
        public string TableName { get; init; } = string.Empty;
        public string ColumnName { get; init; } = string.Empty;
        public string DataType { get; init; } = string.Empty;
        public string UdtName { get; init; } = string.Empty;
        public string IsNullable { get; init; } = string.Empty;
        public string? DefaultSql { get; init; }
        public string? IsGenerated { get; init; }
        public string? GenerationExpression { get; init; }
        public int? MaxLength { get; init; }
        public int? Precision { get; init; }
        public int? Scale { get; init; }
    }

    private sealed class PostgresSchemaPrimaryKeyColumn
    {
        public string TableName { get; init; } = string.Empty;
        public string ColumnName { get; init; } = string.Empty;
        public int Ordinal { get; init; }
    }

    private sealed class PostgresSchemaIndex
    {
        public string TableName { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool IsUnique { get; init; }
        public string ColumnList { get; init; } = string.Empty;
    }
}
