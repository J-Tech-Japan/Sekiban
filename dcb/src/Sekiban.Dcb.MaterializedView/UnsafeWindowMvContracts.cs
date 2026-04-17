using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.MaterializedView;

// ============================================================================
// Unsafe Window Materialized View v1 — public contracts
//
// Design reference: `tasks/unsafe-window-materialized-view/design.md` (PR #1027).
// Implementation issue: #1028.
//
// The public model is "Unsafe Window Materialized View": each projection key has
// at most one row in both the `safe` and `unsafe` tables. Stream events update
// `unsafe`; a promotion worker replays key-scoped events from the event store
// to rebuild the `safe` row once the safe window elapses. Deletes are modelled
// as logical rows (`_is_deleted = true`) so safe promotion replays see them and
// tombstones are retained in `safe`.
// ============================================================================

/// <summary>
///     Describes the business columns of a logical materialized view row. The
///     framework owns the metadata columns (`_projection_key`,
///     `_last_sortable_unique_id`, etc.) and adds them on top of these business
///     columns when generating the safe / unsafe / current / current_live DDL.
/// </summary>
public sealed record UnsafeWindowMvSchema(IReadOnlyList<UnsafeWindowMvColumn> Columns)
{
    public UnsafeWindowMvSchema(params UnsafeWindowMvColumn[] columns) : this((IReadOnlyList<UnsafeWindowMvColumn>)columns)
    {
    }

    /// <summary>
    ///     Column names in schema order, exposed as a convenience for runtime validation.
    /// </summary>
    public IReadOnlyList<string> ColumnNames => Columns.Select(c => c.Name).ToArray();
}

/// <summary>
///     A single business column description. The framework writes values via
///     <see cref="Getter" /> when persisting a row and emits the raw
///     <see cref="SqlType" /> (e.g. <c>TEXT NOT NULL</c>) inside the generated
///     CREATE TABLE statement.
/// </summary>
public sealed record UnsafeWindowMvColumn(string Name, string SqlType, Func<object, object?> Getter);

/// <summary>
///     Apply outcomes produced by a projector for a single event. Using a
///     closed hierarchy (abstract sealed-record with private ctor + nested
///     cases) keeps the framework's handling exhaustive.
/// </summary>
public abstract record UnsafeWindowMvApplyOutcome
{
    private UnsafeWindowMvApplyOutcome()
    {
    }

    /// <summary>
    ///     The event does not affect any row in this projector's view.
    /// </summary>
    public sealed record NoChange : UnsafeWindowMvApplyOutcome;

    /// <summary>
    ///     Upsert a row for <paramref name="ProjectionKey" />. The framework
    ///     extracts column values using the projector's <see cref="UnsafeWindowMvSchema" />.
    /// </summary>
    public sealed record Upsert(string ProjectionKey, object Row) : UnsafeWindowMvApplyOutcome;

    /// <summary>
    ///     Logically delete the row for <paramref name="ProjectionKey" />. The
    ///     row survives in <c>safe</c> as a tombstone (<c>_is_deleted = true</c>)
    ///     and is hidden from <c>current_live</c>.
    /// </summary>
    public sealed record Delete(string ProjectionKey) : UnsafeWindowMvApplyOutcome;
}

/// <summary>
///     Public v1 contract for Unsafe Window materialized views. A projector
///     describes (a) its persistent identity (<see cref="ViewName" /> /
///     <see cref="ViewVersion" />), (b) how long to wait before promoting an
///     unsafe row to safe (<see cref="SafeWindow" />), (c) the business columns
///     it persists (<see cref="Schema" />), (d) a deterministic fold from a
///     previous row and an event to an <see cref="UnsafeWindowMvApplyOutcome" />,
///     and (e) the tags needed to replay events for a projection key during
///     safe promotion.
/// </summary>
/// <typeparam name="TRow">
///     The POCO type the projector uses to represent one logical row. It must
///     expose a parameterless constructor and public settable properties that
///     match the column names in <see cref="Schema" /> (snake_case column → PascalCase property).
/// </typeparam>
public interface IUnsafeWindowMvProjector<TRow> where TRow : class, new()
{
    string ViewName { get; }
    int ViewVersion { get; }
    TimeSpan SafeWindow { get; }
    UnsafeWindowMvSchema Schema { get; }

    /// <summary>
    ///     Extract the projection key this event targets purely from the event
    ///     payload. Returns <c>null</c> if the event is irrelevant to this
    ///     projector. The framework uses this value up-front to locate the
    ///     row in <c>unsafe</c> before calling <see cref="Apply" />.
    ///     Separating this from <see cref="Apply" /> means the framework never
    ///     has to probe <see cref="Apply" /> with a synthetic null row just to
    ///     discover the key — events that only make sense with an existing row
    ///     (e.g. a field update) still expose their key.
    /// </summary>
    string? GetProjectionKey(Event ev);

    /// <summary>
    ///     Deterministic fold function invoked by both the stream apply path
    ///     (with a possibly-null current row and a single incoming event) and the
    ///     safe promotion replay path (starting from the current <c>safe</c> row
    ///     and folding all events since that row's sortable unique id). The same
    ///     method runs on both paths so the two can never drift.
    /// </summary>
    UnsafeWindowMvApplyOutcome Apply(TRow? current, Event ev);

    /// <summary>
    ///     Returns the tags used to fetch events for a given projection key
    ///     during safe promotion. The framework does <c>ReadEventsByTagAsync</c>
    ///     for each tag and deduplicates by event id before folding.
    /// </summary>
    IReadOnlyList<ITag> TagsForProjectionKey(string projectionKey);
}
