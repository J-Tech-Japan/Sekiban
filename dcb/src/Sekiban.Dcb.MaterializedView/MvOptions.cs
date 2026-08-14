using System.Text;

namespace Sekiban.Dcb.MaterializedView;

public sealed class MvOptions
{
    public const string DefaultTablePrefix = "sekiban_mv";
    public static readonly TimeSpan DefaultStreamReorderWindow = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 100;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan StreamReorderWindow { get; set; } = DefaultStreamReorderWindow;
    public int SafeWindowMs { get; set; } = 5000;
    public int MaxConsecutiveFailuresBeforeStop { get; set; } = 3;
    public string TablePrefix { get; set; } = DefaultTablePrefix;
    public PhysicalNameResolver? PhysicalNameResolver { get; set; }

    /// <summary>
    ///     Number of consecutive empty catch-up batches that will cause the grain
    ///     to mark catch-up as ready and stop the per-tick batch execution.
    ///     The default (1) preserves the original "loop until AppliedEvents == 0"
    ///     semantics so that background catch-up does not race with fresh stream
    ///     deliveries once the initial backlog is drained.
    /// </summary>
    public int MaxConsecutiveEmptyBatches { get; set; } = 1;

    /// <summary>
    ///     Duration without catch-up progress after which the orchestration state
    ///     is considered stale and reset so the next scheduled cycle can retry.
    /// </summary>
    public TimeSpan CatchUpStallThreshold { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Maximum number of MV catch-up batches that may execute concurrently
    ///     inside the same process/silo. When the limit is busy, a grain will
    ///     skip its scheduled cycle and retry on the next tick.
    /// </summary>
    public int CatchUpMaxConcurrentBatches { get; set; } = 1;

    /// <summary>
    ///     Optional exact service identity used by the hosted MV worker and by direct executor calls that omit an
    ///     identity. Multi-service hosts should register one service-bound worker per service instead of relying on this
    ///     ambient option.
    /// </summary>
    public string? ServiceId { get; set; }

    /// <summary>
    ///     Explicitly opts a single-service compatibility registration into the literal <c>default</c> identity.
    ///     Ordinary multi-service registrations must supply a non-default service id.
    /// </summary>
    public bool AllowDefaultServiceId { get; set; }

    /// <summary>
    ///     Selects whether initialization may create/ensure schema or may only verify a pre-provisioned schema.
    ///     The compatibility default preserves the historical create/ensure behavior.
    /// </summary>
    public MvInitializationMode InitializationMode { get; set; } = MvInitializationMode.CreateOrEnsure;

    /// <summary>
    ///     Delay before a hosted worker retries a failed VerifyOnly contract check. CreateOrEnsure workers do not use
    ///     this setting. A non-positive value falls back to <see cref="PollInterval"/>.
    /// </summary>
    public TimeSpan VerifyOnlyRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Additive alias for <see cref="InitializationMode"/> using the infrastructure-ownership terminology.
    /// </summary>
    public MvInfrastructureMode InfrastructureMode
    {
        get => (MvInfrastructureMode)InitializationMode;
        set => InitializationMode = (MvInitializationMode)value;
    }

    /// <summary>
    ///     Host-owned policy evaluated for every projector-supplied initialization and apply statement before it is
    ///     sent to the provider.
    /// </summary>
    public IMvSqlStatementPolicy? SqlStatementPolicy { get; set; } = MvAllowAllSqlStatementPolicy.Instance;

    /// <summary>
    ///     Selects whether the compatibility raw apply-context surface remains available or every apply query is
    ///     evaluated before execution through a non-raw policy port.
    /// </summary>
    public MvSqlStatementPolicyMode SqlStatementPolicyMode { get; set; } = MvSqlStatementPolicyMode.Legacy;

    /// <summary>
    ///     Explicit least-privilege SQL Server connection used by the verify-only catalog inspector. The ordinary
    ///     provider connection remains the writable connection used by legacy/create-and-ensure execution. SQL Server
    ///     cannot enforce read-only access with ApplicationIntent on a standalone instance, so the principal must
    ///     have no database/object DML or DDL permissions and enough catalog metadata visibility (for example,
    ///     database VIEW DEFINITION) for the declared contract. Verify-only fails closed when this restricted
    ///     inspection capability is not configured or cannot be established.
    /// </summary>
    public string? SqlServerInspectionConnectionString { get; set; }
}

public static class MvSchemaHelper
{
    public const string LastSortableUniqueIdColumn = "_last_sortable_unique_id";
    public const string LastAppliedAtColumn = "_last_applied_at";

    public static string MetadataColumnsSql(string timestampSql = "NOW()") =>
        $"{LastSortableUniqueIdColumn} TEXT NOT NULL, {LastAppliedAtColumn} TIMESTAMPTZ NOT NULL DEFAULT {timestampSql}";
}

public static class MvPhysicalName
{
    private const int MaximumIdentifierLength = 63;

    public static string Resolve(MvOptions options, string viewName, int viewVersion, string logicalTable)
    {
        var resolver = options.PhysicalNameResolver ?? ((view, version, logical) =>
            $"{SanitizeSegment(options.TablePrefix)}_{SanitizeSegment(view)}_v{version}_{SanitizeSegment(logical)}");
        var resolved = resolver(viewName, viewVersion, logicalTable);
        ValidateIdentifier(resolved);
        return resolved;
    }

    public static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identifier segment cannot be empty.", nameof(value));
        }

        var buffer = new List<char>(value.Length);
        var previousWasUnderscore = false;
        foreach (var current in value.Trim().ToLowerInvariant())
        {
            var normalized = char.IsAsciiLetterOrDigit(current) ? current : '_';
            if (normalized == '_' && previousWasUnderscore)
            {
                continue;
            }

            buffer.Add(normalized);
            previousWasUnderscore = normalized == '_';
        }

        var sanitized = new string(buffer.ToArray()).Trim('_');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new ArgumentException("Identifier segment becomes empty after sanitization.", nameof(value));
        }

        return sanitized;
    }

    public static void ValidateIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier cannot be empty.", nameof(identifier));
        }

        if (Encoding.UTF8.GetByteCount(identifier) > MaximumIdentifierLength)
        {
            throw new ArgumentException(
                $"Identifier '{identifier}' exceeds PostgreSQL's {MaximumIdentifierLength}-byte identifier limit.",
                nameof(identifier));
        }

        if (!(IsAsciiLowerLetter(identifier[0]) || identifier[0] == '_'))
        {
            throw new ArgumentException(
                $"Identifier '{identifier}' must start with a lowercase ASCII letter or underscore.",
                nameof(identifier));
        }

        if (identifier.Any(character => !(IsAsciiLowerLetter(character) || char.IsAsciiDigit(character) || character == '_')))
        {
            throw new ArgumentException(
                $"Identifier '{identifier}' contains characters outside [a-z0-9_].",
                nameof(identifier));
        }
    }

    private static bool IsAsciiLowerLetter(char character) => character is >= 'a' and <= 'z';
}
