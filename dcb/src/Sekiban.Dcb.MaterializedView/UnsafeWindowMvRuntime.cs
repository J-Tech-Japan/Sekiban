using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sekiban.Dcb.MaterializedView;

// ============================================================================
// Unsafe Window Materialized View — provider-neutral runtime pieces (issue #1035).
//
// Each relational provider (Postgres / SQL Server / MySQL / SQLite) owns its
// own SQL dialect, connection type, and DDL/upsert statements. The pieces in
// this file are purely provider-agnostic:
//
//   UnsafeWindowMvReservedColumns — metadata column name constants shared by
//                                   every provider.
//   UnsafeWindowMvLogicalType     — neutral logical SQL types a projector can
//                                   declare; each provider maps them to its
//                                   native dialect when generating DDL.
//   UnsafeWindowMvRowHydrator<T>  — reflection-based POCO hydration from a
//                                   DB row dictionary; uses only projector
//                                   schema + dictionary access, so there is
//                                   no reason to duplicate per provider.
//   IUnsafeWindowMv{Initializer,
//                   CatchUpRunner,
//                   Promoter}     — provider-neutral contracts the hosted
//                                   service depends on. Concrete types in
//                                   each provider package implement these.
//   UnsafeWindowMvHostedService   — the catch-up + promotion loop. Identical
//                                   across providers, so it lives here.
// ============================================================================

/// <summary>
///     Framework-managed metadata column names used by every unsafe-window
///     materialized view, regardless of database provider. Business column
///     names must not collide with any name in here.
/// </summary>
public static class UnsafeWindowMvReservedColumns
{
    public const string ProjectionKey = "_projection_key";
    public const string IsDeleted = "_is_deleted";
    public const string LastSortableUniqueId = "_last_sortable_unique_id";
    public const string LastEventVersion = "_last_event_version";
    public const string LastAppliedAt = "_last_applied_at";
    public const string SafeConfirmedAt = "_safe_confirmed_at";
    public const string UnsafeSince = "_unsafe_since";
    public const string SafeDueAt = "_safe_due_at";
    public const string NeedsRebuild = "_needs_rebuild";

    public static readonly string[] CommonMetadata =
    [
        ProjectionKey,
        IsDeleted,
        LastSortableUniqueId,
        LastEventVersion,
        LastAppliedAt
    ];

    public static readonly string[] SafeOnlyMetadata = [SafeConfirmedAt];

    public static readonly string[] UnsafeOnlyMetadata =
    [
        UnsafeSince,
        SafeDueAt,
        NeedsRebuild
    ];

    public static IEnumerable<string> AllReservedNames =>
        CommonMetadata.Concat(SafeOnlyMetadata).Concat(UnsafeOnlyMetadata);
}

/// <summary>
///     Reflection-based hydrator that rebuilds a TRow POCO from a dictionary
///     of column name -&gt; value pairs (the typical shape a micro-ORM returns
///     for dynamic rows). The hydrator only looks at the projector's declared
///     schema and does not know about database providers, so the same logic
///     can be reused unchanged across every unsafe-window runtime.
/// </summary>
public static class UnsafeWindowMvRowHydrator<TRow> where TRow : class, new()
{
    public static TRow Hydrate(UnsafeWindowMvSchema schema, IDictionary<string, object> dbRow)
    {
        var ctor = typeof(TRow).GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 0)
            ?? throw new InvalidOperationException(
                $"Row type '{typeof(TRow).FullName}' must expose a parameterless constructor so the runtime can rebuild rows read from the unsafe/safe tables.");

        var instance = (TRow)ctor.Invoke(null);
        foreach (var column in schema.Columns)
        {
            if (!dbRow.TryGetValue(column.Name, out var value) || value is null || value is DBNull)
            {
                continue;
            }

            var property = typeof(TRow).GetProperty(PascalCaseFromSnake(column.Name), BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanWrite)
            {
                continue;
            }

            property.SetValue(instance, CoerceValue(value, property.PropertyType));
        }

        return instance;
    }

    private static object? CoerceValue(object value, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying.IsInstanceOfType(value))
        {
            return value;
        }

        if (underlying == typeof(DateOnly))
        {
            return value switch
            {
                DateTime dt => DateOnly.FromDateTime(dt),
                DateTimeOffset dto => DateOnly.FromDateTime(dto.UtcDateTime),
                string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate)
                    => DateOnly.FromDateTime(parsedDate),
                _ => value
            };
        }
        if (underlying == typeof(Guid) && value is string str && Guid.TryParse(str, out var g))
        {
            return g;
        }
        if (underlying == typeof(bool))
        {
            return value switch
            {
                long l => l != 0,
                int i => i != 0,
                short s => s != 0,
                byte b => b != 0,
                sbyte sb => sb != 0,
                _ => value
            };
        }

        try
        {
            return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
        }
        catch
        {
            return value;
        }
    }

    private static string PascalCaseFromSnake(string name)
    {
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                sb.Append(part[1..]);
            }
        }
        return sb.ToString();
    }
}

/// <summary>
///     Provider-neutral "create tables, views and indices for this unsafe
///     window MV" contract. The concrete provider package (Postgres, SQL
///     Server, MySQL, SQLite) implements this with dialect-specific DDL.
/// </summary>
public interface IUnsafeWindowMvInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

/// <summary>
///     Runs one pass of "read events from the event store and apply them to
///     unsafe" for a single projector. Returns the number of events applied
///     in that pass.
/// </summary>
public interface IUnsafeWindowMvCatchUpRunner
{
    Task<int> CatchUpOnceAsync(CancellationToken cancellationToken);
}

/// <summary>
///     Runs one pass of "promote due unsafe rows into safe" for a single
///     projector. Returns the number of keys promoted.
/// </summary>
public interface IUnsafeWindowMvPromoter
{
    Task<int> PromoteOnceAsync(CancellationToken cancellationToken);
}

/// <summary>
///     Provider-neutral BackgroundService that drives the catch-up +
///     promotion loop for a single unsafe-window projector. Providers only
///     need to supply their concrete initializer / catch-up / promoter
///     implementations; the orchestration is identical everywhere.
/// </summary>
public sealed class UnsafeWindowMvHostedService : BackgroundService
{
    private readonly IUnsafeWindowMvInitializer _initializer;
    private readonly IUnsafeWindowMvCatchUpRunner _catchUp;
    private readonly IUnsafeWindowMvPromoter _promoter;
    private readonly TimeSpan _idleDelay;
    private readonly ILogger<UnsafeWindowMvHostedService> _logger;
    private readonly string _viewName;

    public UnsafeWindowMvHostedService(
        IUnsafeWindowMvInitializer initializer,
        IUnsafeWindowMvCatchUpRunner catchUp,
        IUnsafeWindowMvPromoter promoter,
        string viewName,
        ILogger<UnsafeWindowMvHostedService> logger,
        TimeSpan? idleDelay = null)
    {
        _initializer = initializer;
        _catchUp = catchUp;
        _promoter = promoter;
        _viewName = viewName;
        _logger = logger;
        _idleDelay = idleDelay ?? TimeSpan.FromSeconds(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialized = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!initialized)
            {
                // Initialization can fail if the database is not yet reachable
                // at boot. Retry on the loop cadence instead of bailing out so
                // the service recovers as soon as the DB comes up, without
                // requiring the entire host to be restarted.
                try
                {
                    await _initializer.InitializeAsync(stoppingToken).ConfigureAwait(false);
                    initialized = true;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unsafe window MV '{View}' initialization failed; retrying after {Delay}.", _viewName, _idleDelay);
                    try
                    {
                        await Task.Delay(_idleDelay, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    continue;
                }
            }

            var did = 0;
            try
            {
                did += await _catchUp.CatchUpOnceAsync(stoppingToken).ConfigureAwait(false);
                did += await _promoter.PromoteOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unsafe window MV '{View}' iteration failed; retrying after {Delay}.", _viewName, _idleDelay);
            }

            if (did == 0)
            {
                try
                {
                    await Task.Delay(_idleDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}

/// <summary>
///     Maps the small, portable set of business-column type names we accept
///     in <see cref="UnsafeWindowMvColumn.SqlType" /> to a concrete dialect.
///     The goal is that the same projector definition can be used against
///     any supported provider by writing types in a canonical Postgres-style
///     form (<c>UUID NOT NULL</c>, <c>TEXT</c>, <c>TIMESTAMPTZ</c>, …) and
///     letting each provider translate them when generating DDL.
/// </summary>
public static class UnsafeWindowMvLogicalTypes
{
    /// <summary>
    ///     Replace the type keyword at the head of a projector-supplied
    ///     <c>SqlType</c> with its provider-specific equivalent. Constraint
    ///     tail (e.g. <c>NOT NULL</c>, <c>DEFAULT 0</c>) is preserved.
    /// </summary>
    public static string Translate(string declaredType, IReadOnlyDictionary<string, string> keywordMap)
    {
        if (string.IsNullOrWhiteSpace(declaredType))
        {
            return declaredType;
        }

        var trimmed = declaredType.TrimStart();
        // Find the first whitespace boundary after the type keyword (if any).
        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
        {
            end++;
        }

        if (end == 0)
        {
            return declaredType;
        }

        var keyword = trimmed[..end];
        var rest = trimmed[end..];
        if (keywordMap.TryGetValue(keyword.ToUpperInvariant(), out var replacement))
        {
            var leadingWhitespace = declaredType[..(declaredType.Length - trimmed.Length)];
            return $"{leadingWhitespace}{replacement}{rest}";
        }

        return declaredType;
    }
}
