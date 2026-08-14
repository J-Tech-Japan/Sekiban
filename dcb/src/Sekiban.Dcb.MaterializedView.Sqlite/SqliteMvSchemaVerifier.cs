using Dapper;
using Microsoft.Data.Sqlite;
using Sekiban.Dcb.MaterializedView;

namespace Sekiban.Dcb.MaterializedView.Sqlite;

public sealed partial class SqliteMvRegistryStore
{
    private const string TableInfoSql =
        "SELECT name AS Name, type AS Type, \"notnull\" AS \"NotNull\", pk AS Pk, dflt_value AS DefaultSql, hidden AS Hidden FROM pragma_table_xinfo(@TableName);";
    private const string TableDefinitionSql =
        "SELECT sql AS Definition FROM sqlite_master WHERE type = 'table' AND name = @TableName;";

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
                var tableDefinition = await connection.ExecuteScalarAsync<string?>(
                        CatalogCommand(
                            TableDefinitionSql,
                            new { TableName = tableName },
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
                foreach (var column in tableInfo)
                {
                    var dimensions = ParseDeclaredType(column.Type);
                    var generationExpression = column.Hidden is 2 or 3
                        ? ExtractGenerationExpression(tableDefinition, column.Name)
                        : null;
                    columns.Add(new MvObservedSchemaColumn(
                        tableName,
                        column.Name,
                        MapType(column.Name, column.Type),
                        column.NotNull == 0)
                    {
                        DefaultSql = column.DefaultSql,
                        IsGenerated = column.Hidden is 2 or 3,
                        GenerationExpression = generationExpression,
                        MaxLength = dimensions.MaxLength,
                        Precision = dimensions.Precision,
                        Scale = dimensions.Scale
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

            EnsureRequiredMetadataCanBeInspected(requirements, columns);
            return MvSchemaRequirements.Validate(
                requirements,
                MvSchemaRequirements.Observe(columns, primaryKeys, indexes));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnsupportedSchemaCapabilityException ex)
        {
            return MvSchemaVerificationResult.Failed(
                MvInitializationFailureReason.UnsupportedProviderCapability,
                ex.Message,
                ex.LogicalTable,
                ex.PhysicalTable,
                ex.Column);
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

    private static void EnsureRequiredMetadataCanBeInspected(
        IReadOnlyList<MvSchemaTableRequirement> requirements,
        IReadOnlyList<MvObservedSchemaColumn> columns)
    {
        var observedColumns = columns.ToDictionary(
            column => (column.TableName, column.Name),
            StringTupleComparer.Instance);
        foreach (var requirement in requirements)
        {
            foreach (var expected in requirement.Columns)
            {
                if (!observedColumns.TryGetValue((requirement.PhysicalTable, expected.Name), out var actual))
                {
                    continue;
                }

                if (actual.IsGenerated && actual.GenerationExpression is null)
                {
                    throw new UnsupportedSchemaCapabilityException(
                        "SQLite pragma metadata identifies a generated column, but its generation expression could not be derived from sqlite_master.",
                        requirement.LogicalTable,
                        requirement.PhysicalTable,
                        expected.Name);
                }

                if (expected.GenerationExpression is not null && actual.GenerationExpression is null && actual.IsGenerated)
                {
                    throw new UnsupportedSchemaCapabilityException(
                        "SQLite cannot faithfully inspect the required generated-column expression.",
                        requirement.LogicalTable,
                        requirement.PhysicalTable,
                        expected.Name);
                }

                if (expected.MaxLength is not null && actual.MaxLength is null)
                {
                    throw new UnsupportedSchemaCapabilityException(
                        "SQLite cannot faithfully inspect the required declared character or binary length.",
                        requirement.LogicalTable,
                        requirement.PhysicalTable,
                        expected.Name);
                }

                if ((expected.Precision is not null || expected.Scale is not null) &&
                    (actual.Precision is null || actual.Scale is null))
                {
                    throw new UnsupportedSchemaCapabilityException(
                        "SQLite cannot faithfully inspect the required declared numeric precision and scale.",
                        requirement.LogicalTable,
                        requirement.PhysicalTable,
                        expected.Name);
                }
            }
        }
    }

    private static (int? MaxLength, int? Precision, int? Scale) ParseDeclaredType(string? declaredType)
    {
        if (string.IsNullOrWhiteSpace(declaredType))
        {
            return (null, null, null);
        }

        var openParen = declaredType.IndexOf('(');
        var closeParen = declaredType.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen)
        {
            return (null, null, null);
        }

        var baseType = declaredType[..openParen].Trim().ToUpperInvariant();
        var arguments = declaredType[(openParen + 1)..closeParen]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (arguments.Length is < 1 or > 2 || !int.TryParse(arguments[0], out var first) || first < 0)
        {
            return (null, null, null);
        }
        int? second = null;
        if (arguments.Length == 2)
        {
            if (!int.TryParse(arguments[1], out var parsedSecond) || parsedSecond < 0)
            {
                return (null, null, null);
            }

            second = parsedSecond;
        }

        if (baseType is "CHAR" or "CHARACTER" or "VARCHAR" or "NCHAR" or "NVARCHAR" or
            "BINARY" or "VARBINARY" or "CLOB" or "TEXT" or "BLOB")
        {
            return (first, null, null);
        }

        if (baseType is "DECIMAL" or "NUMERIC" or "NUMBER")
        {
            return (null, first, second ?? 0);
        }

        return (null, null, null);
    }

    private static string? ExtractGenerationExpression(string? tableDefinition, string columnName)
    {
        if (string.IsNullOrWhiteSpace(tableDefinition))
        {
            return null;
        }

        var openParen = tableDefinition.IndexOf('(');
        var closeParen = tableDefinition.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen)
        {
            return null;
        }

        foreach (var definition in SplitTopLevelDefinitions(tableDefinition[(openParen + 1)..closeParen]))
        {
            if (!TryReadLeadingIdentifier(definition, out var definitionColumn) ||
                !string.Equals(definitionColumn, columnName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var generatedIndex = definition.IndexOf("GENERATED", StringComparison.OrdinalIgnoreCase);
            var asIndex = generatedIndex >= 0
                ? definition.IndexOf("AS", generatedIndex, StringComparison.OrdinalIgnoreCase)
                : definition.IndexOf(" AS ", StringComparison.OrdinalIgnoreCase);
            if (asIndex < 0)
            {
                return null;
            }

            var expressionStart = definition.IndexOf('(', asIndex);
            if (expressionStart < 0 || !TryReadBalancedExpression(definition, expressionStart, out var expression))
            {
                return null;
            }

            return expression;
        }

        return null;
    }

    private static IReadOnlyList<string> SplitTopLevelDefinitions(string definitions)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < definitions.Length; index++)
        {
            var current = definitions[index];
            if (quote != '\0')
            {
                if (current == quote && (quote != '\'' || index + 1 >= definitions.Length || definitions[index + 1] != '\''))
                {
                    quote = '\0';
                }
                else if (current == quote && quote == '\'')
                {
                    index++;
                }

                continue;
            }

            if (current is '\'' or '"' or '`')
            {
                quote = current;
            }
            else if (current == '(')
            {
                depth++;
            }
            else if (current == ')')
            {
                depth--;
            }
            else if (current == ',' && depth == 0)
            {
                result.Add(definitions[start..index]);
                start = index + 1;
            }
        }

        result.Add(definitions[start..]);
        return result;
    }

    private static bool TryReadLeadingIdentifier(string definition, out string identifier)
    {
        var value = definition.TrimStart();
        if (value.Length == 0 || value.StartsWith("CONSTRAINT ", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("PRIMARY ", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("UNIQUE ", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("CHECK ", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("FOREIGN ", StringComparison.OrdinalIgnoreCase))
        {
            identifier = string.Empty;
            return false;
        }

        var end = 0;
        while (end < value.Length && (IsAsciiLetter(value[end]) || char.IsAsciiDigit(value[end]) || value[end] == '_'))
        {
            end++;
        }

        identifier = end == 0 ? string.Empty : value[..end];
        return end > 0;
    }

    private static bool TryReadBalancedExpression(string value, int openParen, out string expression)
    {
        var depth = 0;
        var quote = '\0';
        for (var index = openParen; index < value.Length; index++)
        {
            var current = value[index];
            if (quote != '\0')
            {
                if (current == quote && (quote != '\'' || index + 1 >= value.Length || value[index + 1] != '\''))
                {
                    quote = '\0';
                }
                else if (current == quote && quote == '\'')
                {
                    index++;
                }

                continue;
            }

            if (current is '\'' or '"' or '`')
            {
                quote = current;
            }
            else if (current == '(')
            {
                depth++;
            }
            else if (current == ')' && --depth == 0)
            {
                expression = value[(openParen + 1)..index].Trim();
                return true;
            }
        }

        expression = string.Empty;
        return false;
    }

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

    private sealed class UnsupportedSchemaCapabilityException(
        string message,
        string? logicalTable,
        string? physicalTable,
        string? column) : Exception(message)
    {
        public string? LogicalTable { get; } = logicalTable;
        public string? PhysicalTable { get; } = physicalTable;
        public string? Column { get; } = column;
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
