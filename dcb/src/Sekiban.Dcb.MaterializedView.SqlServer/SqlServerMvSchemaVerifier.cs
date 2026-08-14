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
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var tableNames = requirements
                .Select(requirement => requirement.PhysicalTable)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var columns = await connection.QueryAsync<SqlServerSchemaColumn>(
                    CatalogCommand(
                        """
                        SELECT TABLE_NAME AS TableName,
                               COLUMN_NAME AS ColumnName,
                               DATA_TYPE AS DataType,
                               IS_NULLABLE AS IsNullable
                        FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_SCHEMA = SCHEMA_NAME()
                          AND TABLE_NAME IN @TableNames;
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

            return MvSchemaRequirements.Validate(
                requirements,
                MvSchemaRequirements.Observe(
                    columns.Select(column => new MvObservedSchemaColumn(
                        column.TableName,
                        column.ColumnName,
                        MapType(column.DataType),
                        string.Equals(column.IsNullable, "YES", StringComparison.OrdinalIgnoreCase))),
                    primaryKeys.Select(column => new MvObservedSchemaPrimaryKeyColumn(
                        column.TableName,
                        column.ColumnName,
                        column.Ordinal))));
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
    }

    private sealed class SqlServerSchemaPrimaryKeyColumn
    {
        public string TableName { get; init; } = string.Empty;
        public string ColumnName { get; init; } = string.Empty;
        public int Ordinal { get; init; }
    }
}
