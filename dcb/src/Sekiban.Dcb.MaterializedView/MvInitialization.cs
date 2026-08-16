namespace Sekiban.Dcb.MaterializedView;

public enum MvInitializationMode
{
    CreateOrEnsure = 0,
    VerifyOnly = 1,
    VerifyAndExecute = 2
}

/// <summary>
///     Additive name for the infrastructure ownership mode. <see cref="MvInitializationMode"/> remains the original
///     public option so existing callers and serialized option values retain their CLR and numeric contract.
/// </summary>
public enum MvInfrastructureMode
{
    EnsureAndInitialize = 0,
    VerifyOnly = 1,
    VerifyAndExecute = 2
}

public enum MvInitializationFailureReason
{
    MissingSchemaObject = 0,
    IncompatibleSchema = 1,
    MissingSchemaContract = 2,
    UnsupportedProviderCapability = 3,
    SchemaContractUnavailable = 4
}

public enum MvSchemaMismatchCode
{
    TableMissing = 0,
    ColumnMissing = 1,
    TypeIncompatible = 2,
    NullabilityMismatch = 3,
    DefaultMismatch = 4,
    PrimaryKeyMismatch = 5,
    BindingMismatch = 6,
    ContractUnavailable = 7,
    RequiredIndexMissing = 8,
    GeneratedSemanticsMismatch = 9,
    SizeMismatch = 10,
    PrecisionMismatch = 11
}

public enum MvSchemaTypeFamily
{
    Any = 0,
    String = 1,
    Integer = 2,
    Boolean = 3,
    DateTime = 4,
    Decimal = 5,
    FloatingPoint = 6,
    Binary = 7,
    Json = 8,
    Guid = 9
}

public sealed record MvInitializationFailure(
    MvInitializationFailureReason Reason,
    string Message,
    string? LogicalTable = null,
    string? PhysicalTable = null,
    string? Column = null)
{
    /// <summary>All deterministic schema mismatches, when verification produced more than one diagnostic.</summary>
    public IReadOnlyList<MvSchemaMismatch> Mismatches { get; init; } = [];
}

public sealed class MvInitializationException : InvalidOperationException
{
    public MvInitializationException(MvInitializationFailure failure)
        : base(failure.Message)
    {
        Failure = failure;
    }

    public MvInitializationFailure Failure { get; }
}

public sealed record MvSchemaColumnRequirement(
    string Name,
    MvSchemaTypeFamily TypeFamily,
    bool IsNullable)
{
    /// <summary>Null means that the contract does not constrain the native default.</summary>
    public string? DefaultSql { get; init; }

    /// <summary>Null means that the contract does not constrain generated-column semantics.</summary>
    public bool? IsGenerated { get; init; }

    /// <summary>Optional normalized generation expression required when <see cref="IsGenerated"/> is true.</summary>
    public string? GenerationExpression { get; init; }

    public int? MaxLength { get; init; }
    public int? Precision { get; init; }
    public int? Scale { get; init; }
}

public sealed record MvSchemaIndexRequirement(
    string Name,
    IReadOnlyList<string> Columns,
    bool IsUnique);

public sealed record MvSchemaTableRequirement(
    string LogicalTable,
    string PhysicalTable,
    IReadOnlyList<MvSchemaColumnRequirement> Columns,
    IReadOnlyList<string> PrimaryKeyColumns)
{
    /// <summary>Required indexes are matched by ordered columns and uniqueness, not provider-specific names.</summary>
    public IReadOnlyList<MvSchemaIndexRequirement> Indexes { get; init; } = [];
}

/// <summary>
///     Versioned, provider-neutral schema contract used by verify-only initialization. The existing table requirement
///     records remain the wire-compatible representation of each contract table.
/// </summary>
public sealed record MvSchemaContract(
    int FormatVersion,
    IReadOnlyList<MvSchemaTableRequirement> Tables)
{
    public const int CurrentFormatVersion = 1;
}

public interface IMvSchemaContractProvider
{
    MvSchemaContract GetSchemaContract(MvDbType databaseType, IMvTableBindings tables);
}

/// <summary>
///     Optional projector contract describing the schema that verify-only initialization must find.
///     The contract is provider-neutral; providers map their native metadata to the type families above.
/// </summary>
public interface IMvSchemaRequirementsProvider
{
    IReadOnlyList<MvSchemaTableRequirement> GetSchemaRequirements(
        MvDbType databaseType,
        IMvTableBindings tables);
}

public sealed record MvObservedSchemaColumn(
    string TableName,
    string Name,
    MvSchemaTypeFamily TypeFamily,
    bool IsNullable)
{
    public string? DefaultSql { get; init; }
    public bool IsGenerated { get; init; }
    public string? GenerationExpression { get; init; }
    public int? MaxLength { get; init; }
    public int? Precision { get; init; }
    public int? Scale { get; init; }
}

public sealed record MvObservedSchemaPrimaryKeyColumn(
    string TableName,
    string Name,
    int Ordinal);

public sealed record MvObservedTableSchema(
    string Name,
    IReadOnlyList<MvObservedSchemaColumn> Columns,
    IReadOnlyList<string> PrimaryKeyColumns)
{
    public IReadOnlyList<MvObservedSchemaIndex> Indexes { get; init; } = [];
}

public sealed record MvObservedSchemaIndex(
    string TableName,
    string Name,
    IReadOnlyList<string> Columns,
    bool IsUnique);

public sealed record MvSchemaMismatch(
    MvSchemaMismatchCode Code,
    string Message,
    string? LogicalTable = null,
    string? PhysicalTable = null,
    string? Column = null);

public sealed record MvSchemaVerificationResult(
    bool IsCompatible,
    MvInitializationFailure? Failure)
{
    public IReadOnlyList<MvSchemaMismatch> Mismatches { get; init; } = [];

    public static MvSchemaVerificationResult Compatible() => new(true, null);

    public static MvSchemaVerificationResult Failed(
        MvInitializationFailureReason reason,
        string message,
        string? logicalTable = null,
        string? physicalTable = null,
        string? column = null) =>
        new(
            false,
            new MvInitializationFailure(reason, message, logicalTable, physicalTable, column));

    public static MvSchemaVerificationResult FailedWithMismatches(
        MvInitializationFailureReason reason,
        IReadOnlyList<MvSchemaMismatch> mismatches)
    {
        var ordered = mismatches
            .OrderBy(mismatch => mismatch.Code)
            .ThenBy(mismatch => mismatch.PhysicalTable, StringComparer.Ordinal)
            .ThenBy(mismatch => mismatch.LogicalTable, StringComparer.Ordinal)
            .ThenBy(mismatch => mismatch.Column, StringComparer.Ordinal)
            .ThenBy(mismatch => mismatch.Message, StringComparer.Ordinal)
            .ToList();
        var first = ordered.FirstOrDefault();
        var failure = new MvInitializationFailure(
            reason,
            first?.Message ?? "Materialized-view schema verification failed.",
            first?.LogicalTable,
            first?.PhysicalTable,
            first?.Column)
        {
            Mismatches = ordered
        };
        return new MvSchemaVerificationResult(false, failure) { Mismatches = ordered };
    }
}

/// <summary>
///     Provider-owned, read-only metadata verification. Implementations must not create, alter, drop, migrate,
///     register, or otherwise mutate database objects while evaluating a request.
/// </summary>
public interface IMvSchemaVerifier
{
    Task<MvSchemaVerificationResult> VerifySchemaAsync(
        IReadOnlyList<MvSchemaTableRequirement> requirements,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Dedicated read-only inspection boundary used by verify-only initialization. Implementations must use catalog
///     reads only; they must not ensure infrastructure, register rows, open a write transaction, or commit.
/// </summary>
public interface IMvReadOnlyMvInspector : IMvSchemaVerifier
{
    Task<IReadOnlyList<MvRegistryEntry>> ReadRegistryEntriesAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the serving pointer through the same provider-owned read-only connection boundary.</summary>
    Task<MvActiveEntry?> ReadActiveAsync(
        string serviceId,
        string viewName,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This read-only materialized-view inspector does not expose the active pointer.");
}

/// <summary>
///     Optional executor capability for an explicit read-only verification request. It is separate from
///     <see cref="IMvExecutor"/> so existing executor implementors do not acquire a new required member.
/// </summary>
public interface IMvInitializationVerifier
{
    Task<MvSchemaVerificationResult> VerifyInitializationAsync(
        IMvApplyHost host,
        string? serviceId = null,
        CancellationToken cancellationToken = default);
}

public static class MvSchemaRequirements
{
    public static IReadOnlyList<MvSchemaTableRequirement> RegistryTables() =>
    [
        new MvSchemaTableRequirement(
            "sekiban_mv_registry",
            "sekiban_mv_registry",
            [
                new("service_id", MvSchemaTypeFamily.String, false),
                new("view_name", MvSchemaTypeFamily.String, false),
                new("view_version", MvSchemaTypeFamily.Integer, false),
                new("logical_table", MvSchemaTypeFamily.String, false),
                new("physical_table", MvSchemaTypeFamily.String, false),
                new("status", MvSchemaTypeFamily.String, false),
                new("current_position", MvSchemaTypeFamily.String, true),
                new("target_position", MvSchemaTypeFamily.String, true),
                new("current_checkpoint_truth", MvSchemaTypeFamily.Any, true),
                new("target_checkpoint_truth", MvSchemaTypeFamily.Any, true),
                new("last_sortable_unique_id", MvSchemaTypeFamily.String, true),
                new("applied_event_version", MvSchemaTypeFamily.Integer, false),
                new("last_applied_source", MvSchemaTypeFamily.String, true),
                new("last_applied_at", MvSchemaTypeFamily.DateTime, true),
                new("last_stream_received_sortable_unique_id", MvSchemaTypeFamily.String, true),
                new("last_stream_received_at", MvSchemaTypeFamily.DateTime, true),
                new("last_stream_applied_sortable_unique_id", MvSchemaTypeFamily.String, true),
                new("last_catch_up_sortable_unique_id", MvSchemaTypeFamily.String, true),
                new("last_updated", MvSchemaTypeFamily.DateTime, false),
                new("metadata", MvSchemaTypeFamily.Any, true)
            ],
            ["service_id", "view_name", "view_version", "logical_table"]),
        new MvSchemaTableRequirement(
            "sekiban_mv_active",
            "sekiban_mv_active",
            [
                new("service_id", MvSchemaTypeFamily.String, false),
                new("view_name", MvSchemaTypeFamily.String, false),
                new("active_version", MvSchemaTypeFamily.Integer, false),
                new("active_generation", MvSchemaTypeFamily.Integer, false),
                new("activated_at", MvSchemaTypeFamily.DateTime, false),
                new("switch_kind", MvSchemaTypeFamily.String, false),
                new("switch_reason", MvSchemaTypeFamily.String, true),
                new("switched_at_utc", MvSchemaTypeFamily.DateTime, true)
            ],
            ["service_id", "view_name"])
    ];

    public static IReadOnlyDictionary<string, MvObservedTableSchema> Observe(
        IEnumerable<MvObservedSchemaColumn> columns,
        IEnumerable<MvObservedSchemaPrimaryKeyColumn> primaryKeyColumns,
        IEnumerable<MvObservedSchemaIndex>? indexes = null)
    {
        var columnGroups = columns
            .GroupBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MvObservedSchemaColumn>)group.ToList(),
                StringComparer.OrdinalIgnoreCase);
        var keyGroups = primaryKeyColumns
            .GroupBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .OrderBy(column => column.Ordinal)
                    .Select(column => column.Name)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
        var indexGroups = (indexes ?? [])
            .GroupBy(index => index.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MvObservedSchemaIndex>)group
                    .OrderBy(index => index.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        return columnGroups.ToDictionary(
            pair => pair.Key,
            pair => new MvObservedTableSchema(
                pair.Key,
                pair.Value,
                keyGroups.TryGetValue(pair.Key, out var keys) ? keys : [])
            {
                Indexes = indexGroups.TryGetValue(pair.Key, out var tableIndexes) ? tableIndexes : []
            },
            StringComparer.OrdinalIgnoreCase);
    }

    public static MvSchemaVerificationResult Validate(
        IReadOnlyList<MvSchemaTableRequirement> requirements,
        IReadOnlyDictionary<string, MvObservedTableSchema> observedTables)
    {
        var mismatches = new List<MvSchemaMismatch>();
        foreach (var requirement in requirements
                     .OrderBy(requirement => requirement.PhysicalTable, StringComparer.Ordinal)
                     .ThenBy(requirement => requirement.LogicalTable, StringComparer.Ordinal))
        {
            if (!observedTables.TryGetValue(requirement.PhysicalTable, out var observed))
            {
                mismatches.Add(
                    new MvSchemaMismatch(
                        MvSchemaMismatchCode.TableMissing,
                        $"Required materialized-view table '{requirement.PhysicalTable}' is missing.",
                        requirement.LogicalTable,
                        requirement.PhysicalTable));
                continue;
            }

            var columns = observed.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var expectedColumn in requirement.Columns.OrderBy(column => column.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!columns.TryGetValue(expectedColumn.Name, out var actualColumn))
                {
                    mismatches.Add(
                        new MvSchemaMismatch(
                            MvSchemaMismatchCode.ColumnMissing,
                            $"Required column '{expectedColumn.Name}' is missing from materialized-view table '{requirement.PhysicalTable}'.",
                            requirement.LogicalTable,
                            requirement.PhysicalTable,
                            expectedColumn.Name));
                    continue;
                }

                if (expectedColumn.TypeFamily != MvSchemaTypeFamily.Any &&
                    actualColumn.TypeFamily != expectedColumn.TypeFamily)
                {
                    mismatches.Add(
                        new MvSchemaMismatch(
                            MvSchemaMismatchCode.TypeIncompatible,
                            $"Column '{expectedColumn.Name}' on materialized-view table '{requirement.PhysicalTable}' has an incompatible type.",
                            requirement.LogicalTable,
                            requirement.PhysicalTable,
                            expectedColumn.Name));
                }

                if (actualColumn.IsNullable != expectedColumn.IsNullable)
                {
                    mismatches.Add(
                        new MvSchemaMismatch(
                            MvSchemaMismatchCode.NullabilityMismatch,
                            $"Column '{expectedColumn.Name}' on materialized-view table '{requirement.PhysicalTable}' has incompatible nullability.",
                            requirement.LogicalTable,
                            requirement.PhysicalTable,
                        expectedColumn.Name));
                }

                if (expectedColumn.DefaultSql is not null &&
                    !string.Equals(
                        NormalizeSqlMetadata(expectedColumn.DefaultSql),
                        NormalizeSqlMetadata(actualColumn.DefaultSql),
                        StringComparison.OrdinalIgnoreCase))
                {
                    mismatches.Add(
                        new MvSchemaMismatch(
                            MvSchemaMismatchCode.DefaultMismatch,
                            $"Column '{expectedColumn.Name}' on materialized-view table '{requirement.PhysicalTable}' has an incompatible default.",
                            requirement.LogicalTable,
                            requirement.PhysicalTable,
                            expectedColumn.Name));
                }

                if (expectedColumn.IsGenerated is { } expectedGenerated &&
                    actualColumn.IsGenerated != expectedGenerated)
                {
                    mismatches.Add(
                        new MvSchemaMismatch(
                            MvSchemaMismatchCode.GeneratedSemanticsMismatch,
                            $"Column '{expectedColumn.Name}' on materialized-view table '{requirement.PhysicalTable}' has incompatible generated-column semantics.",
                            requirement.LogicalTable,
                            requirement.PhysicalTable,
                            expectedColumn.Name));
                }

                if (expectedColumn.GenerationExpression is not null &&
                    !string.Equals(
                        NormalizeSqlMetadata(expectedColumn.GenerationExpression),
                        NormalizeSqlMetadata(actualColumn.GenerationExpression),
                        StringComparison.OrdinalIgnoreCase))
                {
                    mismatches.Add(
                        new MvSchemaMismatch(
                            MvSchemaMismatchCode.GeneratedSemanticsMismatch,
                            $"Column '{expectedColumn.Name}' on materialized-view table '{requirement.PhysicalTable}' has an incompatible generation expression.",
                            requirement.LogicalTable,
                            requirement.PhysicalTable,
                            expectedColumn.Name));
                }

                if (expectedColumn.MaxLength is { } maxLength && actualColumn.MaxLength != maxLength)
                {
                    mismatches.Add(
                        new MvSchemaMismatch(
                            MvSchemaMismatchCode.SizeMismatch,
                            $"Column '{expectedColumn.Name}' on materialized-view table '{requirement.PhysicalTable}' has an incompatible size.",
                            requirement.LogicalTable,
                            requirement.PhysicalTable,
                            expectedColumn.Name));
                }

                if ((expectedColumn.Precision is { } precision && actualColumn.Precision != precision) ||
                    (expectedColumn.Scale is { } scale && actualColumn.Scale != scale))
                {
                    mismatches.Add(
                        new MvSchemaMismatch(
                            MvSchemaMismatchCode.PrecisionMismatch,
                            $"Column '{expectedColumn.Name}' on materialized-view table '{requirement.PhysicalTable}' has incompatible precision or scale.",
                            requirement.LogicalTable,
                            requirement.PhysicalTable,
                            expectedColumn.Name));
                }
            }

            if (!requirement.PrimaryKeyColumns.SequenceEqual(observed.PrimaryKeyColumns, StringComparer.OrdinalIgnoreCase))
            {
                mismatches.Add(
                    new MvSchemaMismatch(
                        MvSchemaMismatchCode.PrimaryKeyMismatch,
                        $"Materialized-view table '{requirement.PhysicalTable}' has an incompatible primary key.",
                        requirement.LogicalTable,
                        requirement.PhysicalTable));
            }

            foreach (var expectedIndex in requirement.Indexes.OrderBy(index => index.Name, StringComparer.Ordinal))
            {
                var found = observed.Indexes.Any(actualIndex =>
                    actualIndex.IsUnique == expectedIndex.IsUnique &&
                    expectedIndex.Columns.SequenceEqual(actualIndex.Columns, StringComparer.OrdinalIgnoreCase));
                if (!found)
                {
                    mismatches.Add(
                        new MvSchemaMismatch(
                            MvSchemaMismatchCode.RequiredIndexMissing,
                            $"Required {(expectedIndex.IsUnique ? "unique " : string.Empty)}index '{expectedIndex.Name}' is missing from materialized-view table '{requirement.PhysicalTable}'.",
                            requirement.LogicalTable,
                            requirement.PhysicalTable));
                }
            }
        }

        if (mismatches.Count == 0)
        {
            return MvSchemaVerificationResult.Compatible();
        }

        var reason = mismatches.Any(mismatch =>
                mismatch.Code is MvSchemaMismatchCode.TableMissing or MvSchemaMismatchCode.ColumnMissing)
            ? MvInitializationFailureReason.MissingSchemaObject
            : MvInitializationFailureReason.IncompatibleSchema;
        return MvSchemaVerificationResult.FailedWithMismatches(reason, mismatches);
    }

    private static string NormalizeSqlMetadata(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = string.Join(
            ' ',
            value.Trim().TrimEnd(';').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        while (normalized.Length >= 2 && normalized[0] == '(' && normalized[^1] == ')')
        {
            normalized = normalized[1..^1].Trim();
        }

        return normalized;
    }

    public static MvSchemaVerificationResult ValidateContract(
        IReadOnlyList<MvTable> tables,
        IReadOnlyList<MvSchemaTableRequirement> requirements)
    {
        var mismatches = new List<MvSchemaMismatch>();
        if (tables.Count != requirements.Count)
        {
            mismatches.Add(
                new MvSchemaMismatch(
                    MvSchemaMismatchCode.ContractUnavailable,
                    "Verify-only initialization requires one schema requirement for every registered materialized-view table."));
        }

        foreach (var duplicateLogicalTable in requirements
                     .GroupBy(requirement => requirement.LogicalTable, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            mismatches.Add(
                new MvSchemaMismatch(
                    MvSchemaMismatchCode.BindingMismatch,
                    $"Verify-only initialization has duplicate schema requirements for logical table '{duplicateLogicalTable.Key}'.",
                    duplicateLogicalTable.Key));
        }

        var requirementsByLogicalName = requirements
            .GroupBy(requirement => requirement.LogicalTable, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var table in tables.OrderBy(table => table.LogicalName, StringComparer.Ordinal))
        {
            if (!requirementsByLogicalName.TryGetValue(table.LogicalName, out var requirement) ||
                !string.Equals(table.PhysicalName, requirement.PhysicalTable, StringComparison.Ordinal))
            {
                mismatches.Add(
                    new MvSchemaMismatch(
                        MvSchemaMismatchCode.BindingMismatch,
                        $"Verify-only initialization has no schema requirement for logical table '{table.LogicalName}'.",
                        table.LogicalName,
                        table.PhysicalName));
                continue;
            }

            if (requirement.Columns.Count == 0 || requirement.PrimaryKeyColumns.Count == 0)
            {
                mismatches.Add(
                    new MvSchemaMismatch(
                        MvSchemaMismatchCode.ContractUnavailable,
                        $"Verify-only initialization requires columns and a primary key for logical table '{table.LogicalName}'.",
                        table.LogicalName,
                        table.PhysicalName));
            }
        }

        return mismatches.Count == 0
            ? MvSchemaVerificationResult.Compatible()
            : MvSchemaVerificationResult.FailedWithMismatches(
                MvInitializationFailureReason.MissingSchemaContract,
                mismatches);
    }
}
