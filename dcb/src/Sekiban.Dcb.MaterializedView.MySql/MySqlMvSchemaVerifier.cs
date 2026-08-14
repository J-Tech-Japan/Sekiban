using Dapper;
using MySqlConnector;
using Sekiban.Dcb.MaterializedView;

namespace Sekiban.Dcb.MaterializedView.MySql;

public sealed partial class MySqlMvRegistryStore
{
    public async Task<MvSchemaVerificationResult> VerifySchemaAsync(
        IReadOnlyList<MvSchemaTableRequirement> requirements,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var tableNames = requirements
                .Select(requirement => requirement.PhysicalTable)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var columns = await connection.QueryAsync<MySqlSchemaColumn>(
                    CatalogCommand(
                        """
                        SELECT table_name AS TableName,
                               column_name AS ColumnName,
                               data_type AS DataType,
                               column_type AS ColumnType,
                               is_nullable AS IsNullable
                        FROM information_schema.columns
                        WHERE table_schema = DATABASE()
                          AND table_name IN @TableNames;
                        """,
                        new { TableNames = tableNames },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            var primaryKeys = await connection.QueryAsync<MySqlSchemaPrimaryKeyColumn>(
                    CatalogCommand(
                        """
                        SELECT table_name AS TableName,
                               column_name AS ColumnName,
                               ordinal_position AS Ordinal
                        FROM information_schema.key_column_usage
                        WHERE constraint_schema = DATABASE()
                          AND constraint_name = 'PRIMARY'
                          AND table_name IN @TableNames;
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
                        MapType(column.DataType, column.ColumnType),
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
                "MySQL schema metadata verification is unavailable.");
        }
    }

    private static MvSchemaTypeFamily MapType(string dataType, string columnType)
    {
        var type = $"{dataType} {columnType}".ToLowerInvariant();
        if (type.Contains("json", StringComparison.Ordinal)) return MvSchemaTypeFamily.Json;
        if (type.Contains("tinyint(1)", StringComparison.Ordinal) || type.Contains("boolean", StringComparison.Ordinal) ||
            type.Contains("bool", StringComparison.Ordinal)) return MvSchemaTypeFamily.Boolean;
        if (type.Contains("datetime", StringComparison.Ordinal) || type.Contains("timestamp", StringComparison.Ordinal) ||
            type.Contains("date", StringComparison.Ordinal) || type.Contains("time", StringComparison.Ordinal))
            return MvSchemaTypeFamily.DateTime;
        if (type.Contains("int", StringComparison.Ordinal) || type.Contains("bit", StringComparison.Ordinal))
            return MvSchemaTypeFamily.Integer;
        if (type.Contains("decimal", StringComparison.Ordinal) || type.Contains("numeric", StringComparison.Ordinal))
            return MvSchemaTypeFamily.Decimal;
        if (type.Contains("double", StringComparison.Ordinal) || type.Contains("float", StringComparison.Ordinal))
            return MvSchemaTypeFamily.FloatingPoint;
        if (type.Contains("blob", StringComparison.Ordinal) || type.Contains("binary", StringComparison.Ordinal))
            return MvSchemaTypeFamily.Binary;
        if (type.Contains("char", StringComparison.Ordinal) || type.Contains("text", StringComparison.Ordinal))
            return MvSchemaTypeFamily.String;
        return MvSchemaTypeFamily.Any;
    }

    private sealed class MySqlSchemaColumn
    {
        public string TableName { get; init; } = string.Empty;
        public string ColumnName { get; init; } = string.Empty;
        public string DataType { get; init; } = string.Empty;
        public string ColumnType { get; init; } = string.Empty;
        public string IsNullable { get; init; } = string.Empty;
    }

    private sealed class MySqlSchemaPrimaryKeyColumn
    {
        public string TableName { get; init; } = string.Empty;
        public string ColumnName { get; init; } = string.Empty;
        public int Ordinal { get; init; }
    }
}
