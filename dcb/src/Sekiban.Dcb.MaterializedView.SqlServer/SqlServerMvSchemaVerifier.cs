using Dapper;
using Microsoft.Data.SqlClient;
using Sekiban.Dcb.MaterializedView;

namespace Sekiban.Dcb.MaterializedView.SqlServer;

public sealed partial class SqlServerMvRegistryStore
{
    public async Task<MvSchemaVerificationResult> VerifySchemaAsync(
        IReadOnlyList<MvSchemaTableRequirement> requirements,
        CancellationToken cancellationToken = default)
    {
        try
        {
            RecordReadOnlyConnection();
            await using var connection = new SqlConnection(ReadOnlyConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var tableNames = requirements
                .Select(requirement => requirement.PhysicalTable)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var columns = await connection.QueryAsync<SqlServerSchemaColumn>(
                    CatalogCommand(
                        """
                        SELECT information_columns.TABLE_NAME AS TableName,
                               information_columns.COLUMN_NAME AS ColumnName,
                               information_columns.DATA_TYPE AS DataType,
                               information_columns.IS_NULLABLE AS IsNullable,
                               information_columns.COLUMN_DEFAULT AS DefaultSql,
                               system_columns.is_computed AS IsGenerated,
                               computed_columns.definition AS GenerationExpression,
                               CASE WHEN information_columns.CHARACTER_MAXIMUM_LENGTH = -1 THEN NULL ELSE information_columns.CHARACTER_MAXIMUM_LENGTH END AS MaxLength,
                               information_columns.NUMERIC_PRECISION AS Precision,
                               information_columns.NUMERIC_SCALE AS Scale
                        FROM INFORMATION_SCHEMA.COLUMNS AS information_columns
                        INNER JOIN sys.tables AS system_tables
                            ON system_tables.name = information_columns.TABLE_NAME
                        INNER JOIN sys.schemas AS schemas
                            ON schemas.schema_id = system_tables.schema_id
                           AND schemas.name = information_columns.TABLE_SCHEMA
                        INNER JOIN sys.columns AS system_columns
                            ON system_columns.object_id = system_tables.object_id
                           AND system_columns.name = information_columns.COLUMN_NAME
                        LEFT JOIN sys.computed_columns AS computed_columns
                            ON computed_columns.object_id = system_columns.object_id
                           AND computed_columns.column_id = system_columns.column_id
                        WHERE information_columns.TABLE_SCHEMA = SCHEMA_NAME()
                          AND information_columns.TABLE_NAME IN @TableNames;
                        """,
                        new { TableNames = tableNames },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            var primaryKeys = await connection.QueryAsync<SqlServerSchemaPrimaryKeyColumn>(
                    CatalogCommand(
                        """
                        SELECT tables.name AS TableName,
                               columns.name AS ColumnName,
                               index_columns.key_ordinal AS Ordinal
                        FROM sys.tables AS tables
                        INNER JOIN sys.indexes AS indexes
                            ON indexes.object_id = tables.object_id
                           AND indexes.is_primary_key = 1
                        INNER JOIN sys.index_columns AS index_columns
                            ON index_columns.object_id = indexes.object_id
                           AND index_columns.index_id = indexes.index_id
                        INNER JOIN sys.columns AS columns
                            ON columns.object_id = index_columns.object_id
                           AND columns.column_id = index_columns.column_id
                        WHERE SCHEMA_NAME(tables.schema_id) = SCHEMA_NAME()
                          AND tables.name IN @TableNames;
                        """,
                        new { TableNames = tableNames },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            var indexes = await connection.QueryAsync<SqlServerSchemaIndexColumn>(
                    CatalogCommand(
                        """
                        SELECT tables.name AS TableName,
                               indexes.name AS Name,
                               indexes.is_unique AS IsUnique,
                               columns.name AS ColumnName,
                               index_columns.key_ordinal AS Ordinal
                        FROM sys.tables AS tables
                        INNER JOIN sys.indexes AS indexes
                            ON indexes.object_id = tables.object_id
                           AND indexes.is_primary_key = 0
                           AND indexes.is_unique_constraint = 0
                        INNER JOIN sys.index_columns AS index_columns
                            ON index_columns.object_id = indexes.object_id
                           AND index_columns.index_id = indexes.index_id
                           AND index_columns.key_ordinal > 0
                        INNER JOIN sys.columns AS columns
                            ON columns.object_id = index_columns.object_id
                           AND columns.column_id = index_columns.column_id
                        WHERE SCHEMA_NAME(tables.schema_id) = SCHEMA_NAME()
                          AND tables.name IN @TableNames
                        ORDER BY tables.name, indexes.name, index_columns.key_ordinal;
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
                        MapType(column.DataType),
                        string.Equals(column.IsNullable, "YES", StringComparison.OrdinalIgnoreCase))
                    {
                        DefaultSql = column.DefaultSql,
                        IsGenerated = column.IsGenerated,
                        GenerationExpression = column.GenerationExpression,
                        MaxLength = column.MaxLength,
                        Precision = column.Precision,
                        Scale = column.Scale
                    }),
                    primaryKeys.Select(column => new MvObservedSchemaPrimaryKeyColumn(
                        column.TableName,
                        column.ColumnName,
                        column.Ordinal)),
                    indexes
                        .GroupBy(index => (index.TableName, index.Name), StringTupleComparer.Instance)
                        .Select(group => new MvObservedSchemaIndex(
                            group.Key.TableName,
                            group.Key.Name,
                            group.OrderBy(index => index.Ordinal).Select(index => index.ColumnName).ToList(),
                            group.First().IsUnique))));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return MvSchemaVerificationResult.Failed(
                MvInitializationFailureReason.UnsupportedProviderCapability,
                "SQL Server schema metadata verification is unavailable.");
        }
    }

    private static MvSchemaTypeFamily MapType(string dataType)
    {
        var type = dataType.ToLowerInvariant();
        if (type is "uniqueidentifier") return MvSchemaTypeFamily.Guid;
        if (type is "bit") return MvSchemaTypeFamily.Boolean;
        if (type.Contains("date", StringComparison.Ordinal) || type.Contains("time", StringComparison.Ordinal))
            return MvSchemaTypeFamily.DateTime;
        if (type is "tinyint" or "smallint" or "int" or "bigint") return MvSchemaTypeFamily.Integer;
        if (type is "decimal" or "numeric" or "money" or "smallmoney") return MvSchemaTypeFamily.Decimal;
        if (type is "float" or "real") return MvSchemaTypeFamily.FloatingPoint;
        if (type.Contains("binary", StringComparison.Ordinal) || type is "image") return MvSchemaTypeFamily.Binary;
        if (type.Contains("char", StringComparison.Ordinal) || type.Contains("text", StringComparison.Ordinal))
            return MvSchemaTypeFamily.String;
        return MvSchemaTypeFamily.Any;
    }

    private sealed class SqlServerSchemaColumn
    {
        public string TableName { get; init; } = string.Empty;
        public string ColumnName { get; init; } = string.Empty;
        public string DataType { get; init; } = string.Empty;
        public string IsNullable { get; init; } = string.Empty;
        public string? DefaultSql { get; init; }
        public bool IsGenerated { get; init; }
        public string? GenerationExpression { get; init; }
        public int? MaxLength { get; init; }
        public int? Precision { get; init; }
        public int? Scale { get; init; }
    }

    private sealed class SqlServerSchemaPrimaryKeyColumn
    {
        public string TableName { get; init; } = string.Empty;
        public string ColumnName { get; init; } = string.Empty;
        public int Ordinal { get; init; }
    }

    private sealed class SqlServerSchemaIndexColumn
    {
        public string TableName { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool IsUnique { get; init; }
        public string ColumnName { get; init; } = string.Empty;
        public int Ordinal { get; init; }
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string TableName, string Name)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals((string TableName, string Name) x, (string TableName, string Name) y) =>
            string.Equals(x.TableName, y.TableName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string TableName, string Name) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TableName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}
