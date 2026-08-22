namespace Sekiban.Dcb.TestSupport;

/// <summary>One production test-only seam (a settable hook / probe) and where it is declared.</summary>
public readonly record struct SeamEntry(string AssemblyName, string DeclaringTypeFullName, string PropertyName);

/// <summary>
///     The SINGLE exhaustive inventory of every production test-only seam (response-loss hooks + allocator/budget probes),
///     mapped to its declaring type and assembly. Every structural guard (exact setter / backing-field / API / ctor /
///     options / DI / IVT) is driven from this one list, so a new seam that is not added here makes the inventory snapshot
///     assertion fail — omission is structurally visible rather than silently unguarded.
/// </summary>
public static class SeamInventory
{
    public const string CoreAssembly = "Sekiban.Dcb.Core";
    public const string PostgresAssembly = "Sekiban.Dcb.Postgres";
    public const string SqliteAssembly = "Sekiban.Dcb.Sqlite";
    public const string CosmosAssembly = "Sekiban.Dcb.CosmosDb";

    public static readonly IReadOnlyList<SeamEntry> Entries = new[]
    {
        new SeamEntry(PostgresAssembly, "Sekiban.Dcb.Postgres.PostgresEventStore", "AfterConditionalCommitHook"),
        new SeamEntry(PostgresAssembly, "Sekiban.Dcb.Postgres.PostgresEventStore", "TagHeadProtocolHook"),
        new SeamEntry(SqliteAssembly, "Sekiban.Dcb.Sqlite.SqliteEventStore", "AfterConditionalCommitHook"),
        new SeamEntry(CosmosAssembly, "Sekiban.Dcb.CosmosDb.CosmosDbEventStore", "AfterConditionalCommitHook"),
        new SeamEntry(CoreAssembly, "Sekiban.Dcb.Actors.CoreGeneralSekibanExecutor", "ConditionalEventIdFactory"),
        new SeamEntry(CoreAssembly, "Sekiban.Dcb.Actors.CoreGeneralSekibanExecutor", "ConditionalSortableIdFactory"),
        new SeamEntry(CoreAssembly, "Sekiban.Dcb.Storage.ConditionalAppendCoordinator", "VerificationBudgetOverride")
    };

    /// <summary>The seam property names declared in the given assembly (drives per-assembly exact target scanning).</summary>
    public static string[] PropertyNamesIn(string assemblyName) =>
        Entries.Where(e => e.AssemblyName == assemblyName).Select(e => e.PropertyName).Distinct().ToArray();

    /// <summary>Every assembly that must be reflection-scanned for assignment (includes ones with no settable seam).</summary>
    public static readonly IReadOnlyList<string> ReflectionScannedAssemblies = new[]
    {
        CoreAssembly, PostgresAssembly, SqliteAssembly, CosmosAssembly, "Sekiban.Dcb.DynamoDB"
    };
}
