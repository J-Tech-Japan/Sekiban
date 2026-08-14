namespace Sekiban.Dcb.MaterializedView;

public enum MvInitializationMode
{
    CreateOrEnsure = 0,
    VerifyOnly = 1
}

public enum MvInitializationFailureReason
{
    MissingSchemaObject = 0,
    IncompatibleSchema = 1,
    MissingSchemaContract = 2,
    UnsupportedProviderCapability = 3
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
    string? Column = null);

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
    bool IsNullable);

public sealed record MvSchemaTableRequirement(
    string LogicalTable,
    string PhysicalTable,
    IReadOnlyList<MvSchemaColumnRequirement> Columns,
    IReadOnlyList<string> PrimaryKeyColumns);

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
    bool IsNullable);

public sealed record MvObservedSchemaPrimaryKeyColumn(
    string TableName,
    string Name,
    int Ordinal);

public sealed record MvObservedTableSchema(
    string Name,
    IReadOnlyList<MvObservedSchemaColumn> Columns,
    IReadOnlyList<string> PrimaryKeyColumns);

public sealed record MvSchemaVerificationResult(
    bool IsCompatible,
    MvInitializationFailure? Failure)
{
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
        IEnumerable<MvObservedSchemaPrimaryKeyColumn> primaryKeyColumns)
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

        return columnGroups.ToDictionary(
            pair => pair.Key,
            pair => new MvObservedTableSchema(
                pair.Key,
                pair.Value,
                keyGroups.TryGetValue(pair.Key, out var keys) ? keys : []),
            StringComparer.OrdinalIgnoreCase);
    }

    public static MvSchemaVerificationResult Validate(
        IReadOnlyList<MvSchemaTableRequirement> requirements,
        IReadOnlyDictionary<string, MvObservedTableSchema> observedTables)
    {
        foreach (var requirement in requirements)
        {
            if (!observedTables.TryGetValue(requirement.PhysicalTable, out var observed))
            {
                return MvSchemaVerificationResult.Failed(
                    MvInitializationFailureReason.MissingSchemaObject,
                    $"Required materialized-view table '{requirement.PhysicalTable}' is missing.",
                    requirement.LogicalTable,
                    requirement.PhysicalTable);
            }

            var columns = observed.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var expectedColumn in requirement.Columns)
            {
                if (!columns.TryGetValue(expectedColumn.Name, out var actualColumn))
                {
                    return MvSchemaVerificationResult.Failed(
                        MvInitializationFailureReason.MissingSchemaObject,
                        $"Required column '{expectedColumn.Name}' is missing from materialized-view table '{requirement.PhysicalTable}'.",
                        requirement.LogicalTable,
                        requirement.PhysicalTable,
                        expectedColumn.Name);
                }

                if (expectedColumn.TypeFamily != MvSchemaTypeFamily.Any &&
                    actualColumn.TypeFamily != expectedColumn.TypeFamily)
                {
                    return MvSchemaVerificationResult.Failed(
                        MvInitializationFailureReason.IncompatibleSchema,
                        $"Column '{expectedColumn.Name}' on materialized-view table '{requirement.PhysicalTable}' has an incompatible type.",
                        requirement.LogicalTable,
                        requirement.PhysicalTable,
                        expectedColumn.Name);
                }

                if (actualColumn.IsNullable != expectedColumn.IsNullable)
                {
                    return MvSchemaVerificationResult.Failed(
                        MvInitializationFailureReason.IncompatibleSchema,
                        $"Column '{expectedColumn.Name}' on materialized-view table '{requirement.PhysicalTable}' has incompatible nullability.",
                        requirement.LogicalTable,
                        requirement.PhysicalTable,
                        expectedColumn.Name);
                }
            }

            if (!requirement.PrimaryKeyColumns.SequenceEqual(observed.PrimaryKeyColumns, StringComparer.OrdinalIgnoreCase))
            {
                return MvSchemaVerificationResult.Failed(
                    MvInitializationFailureReason.IncompatibleSchema,
                    $"Materialized-view table '{requirement.PhysicalTable}' has an incompatible primary key.",
                    requirement.LogicalTable,
                    requirement.PhysicalTable);
            }
        }

        return MvSchemaVerificationResult.Compatible();
    }

    public static MvSchemaVerificationResult ValidateContract(
        IReadOnlyList<MvTable> tables,
        IReadOnlyList<MvSchemaTableRequirement> requirements)
    {
        if (tables.Count != requirements.Count)
        {
            return MvSchemaVerificationResult.Failed(
                MvInitializationFailureReason.MissingSchemaContract,
                "Verify-only initialization requires one schema requirement for every registered materialized-view table.");
        }

        var duplicateLogicalTable = requirements
            .GroupBy(requirement => requirement.LogicalTable, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateLogicalTable is not null)
        {
            return MvSchemaVerificationResult.Failed(
                MvInitializationFailureReason.MissingSchemaContract,
                $"Verify-only initialization has duplicate schema requirements for logical table '{duplicateLogicalTable.Key}'.");
        }

        var requirementsByLogicalName = requirements.ToDictionary(
            requirement => requirement.LogicalTable,
            StringComparer.Ordinal);
        foreach (var table in tables)
        {
            if (!requirementsByLogicalName.TryGetValue(table.LogicalName, out var requirement) ||
                !string.Equals(table.PhysicalName, requirement.PhysicalTable, StringComparison.Ordinal))
            {
                return MvSchemaVerificationResult.Failed(
                    MvInitializationFailureReason.MissingSchemaContract,
                    $"Verify-only initialization has no schema requirement for logical table '{table.LogicalName}'.",
                    table.LogicalName,
                    table.PhysicalName);
            }

            if (requirement.Columns.Count == 0 || requirement.PrimaryKeyColumns.Count == 0)
            {
                return MvSchemaVerificationResult.Failed(
                    MvInitializationFailureReason.MissingSchemaContract,
                    $"Verify-only initialization requires columns and a primary key for logical table '{table.LogicalName}'.",
                    table.LogicalName,
                    table.PhysicalName);
            }
        }

        return MvSchemaVerificationResult.Compatible();
    }
}
