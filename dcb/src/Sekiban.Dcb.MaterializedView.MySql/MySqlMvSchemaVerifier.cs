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
            RecordReadOnlyConnection();
            await using var connection = new MySqlConnection(ReadOnlyConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await SetReadOnlySessionAsync(connection, cancellationToken).ConfigureAwait(false);
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
                               is_nullable AS IsNullable,
                               column_default AS DefaultSql,
                               extra AS Extra,
                               generation_expression AS GenerationExpression,
                               character_maximum_length AS MaxLength,
                               numeric_precision AS NumericPrecision,
                               numeric_scale AS NumericScale
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
            var indexes = await connection.QueryAsync<MySqlSchemaIndexColumn>(
                    CatalogCommand(
                        """
                        SELECT table_name AS TableName,
                               index_name AS Name,
                               non_unique AS NonUnique,
                               column_name AS ColumnName,
                               seq_in_index AS Ordinal
                        FROM information_schema.statistics
                        WHERE table_schema = DATABASE()
                          AND table_name IN @TableNames
                          AND index_name <> 'PRIMARY'
                        ORDER BY table_name, index_name, seq_in_index;
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
                        string.Equals(column.IsNullable, "YES", StringComparison.OrdinalIgnoreCase))
                    {
                        DefaultSql = column.DefaultSql,
                        IsGenerated = column.Extra.Contains("GENERATED", StringComparison.OrdinalIgnoreCase),
                        GenerationExpression = column.GenerationExpression,
                        MaxLength = column.MaxLength is <= int.MaxValue ? (int?)column.MaxLength : null,
                        Precision = column.NumericPrecision,
                        Scale = column.NumericScale
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
                            group.First().NonUnique == 0))));
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
        public string? DefaultSql { get; init; }
        public string Extra { get; init; } = string.Empty;
        public string? GenerationExpression { get; init; }
        public long? MaxLength { get; init; }
        public int? NumericPrecision { get; init; }
        public int? NumericScale { get; init; }
    }

    private sealed class MySqlSchemaPrimaryKeyColumn
    {
        public string TableName { get; init; } = string.Empty;
        public string ColumnName { get; init; } = string.Empty;
        public int Ordinal { get; init; }
    }

    private sealed class MySqlSchemaIndexColumn
    {
        public string TableName { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int NonUnique { get; init; }
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
