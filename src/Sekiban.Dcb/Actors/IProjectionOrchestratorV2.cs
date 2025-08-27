using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Enhanced orchestrator interface with all business logic moved from Grain
/// </summary>
public interface IProjectionOrchestratorV2
{
    /// <summary>
    ///     Initialize the orchestrator with optional persisted state
    /// </summary>
    Task<ResultBox<ProjectionState>> InitializeAsync(string projectorName, SerializedProjectionState? persistedState = null);

    /// <summary>
    ///     Process events with full business logic
    /// </summary>
    Task<ResultBox<ProcessResultV2>> ProcessEventsAsync(IReadOnlyList<Event> events);

    /// <summary>
    ///     Process a single streaming event
    /// </summary>
    Task<ResultBox<ProcessResultV2>> ProcessStreamEventAsync(Event evt);

    /// <summary>
    ///     Determine if persistence is needed based on business rules
    /// </summary>
    Task<PersistenceDecision> ShouldPersistAsync();

    /// <summary>
    ///     Check if an event should be processed (duplicate check)
    /// </summary>
    Task<bool> ShouldProcessEventAsync(Event evt);

    /// <summary>
    ///     Get the current state for persistence
    /// </summary>
    Task<ResultBox<SerializedProjectionStateV2>> GetSerializableStateAsync(bool canGetUnsafeState = true);

    /// <summary>
    ///     Get current projection state
    /// </summary>
    ProjectionState? GetCurrentState();

    /// <summary>
    ///     Check if state size exceeds limits
    /// </summary>
    Task<StateSizeCheck> CheckStateSizeAsync();

    /// <summary>
    ///     Configure orchestrator settings
    /// </summary>
    void Configure(OrchestratorConfiguration config);
}

/// <summary>
///     Enhanced process result with business logic decisions
/// </summary>
public record ProcessResultV2(
    int EventsProcessed,
    int EventsSkipped,              // Duplicates or filtered events
    string? LastPosition,
    string? SafePosition,
    bool RequiresPersistence,        // Business logic decision
    PersistenceReason? PersistReason,
    TimeSpan ProcessingTime);

/// <summary>
///     Reason for persistence recommendation
/// </summary>
public enum PersistenceReason
{
    BatchSizeReached,
    CatchUpComplete,
    SafeWindowPassed,
    PeriodicCheckpoint,
    Shutdown,
    Manual
}

/// <summary>
///     Decision about whether to persist
/// </summary>
public record PersistenceDecision(
    bool ShouldPersist,
    PersistenceReason? Reason,
    int EventsSinceLastPersist,
    TimeSpan TimeSinceLastPersist);

/// <summary>
///     State size check result
/// </summary>
public record StateSizeCheck(
    long CurrentSize,
    long MaxSize,
    bool ExceedsLimit,
    string? Warning);

/// <summary>
///     Enhanced serialized projection state
/// </summary>
public record SerializedProjectionStateV2
{
    public string PayloadJson { get; init; } = string.Empty;
    public string? LastPosition { get; init; }
    public string? SafePosition { get; init; }
    public long EventsProcessed { get; init; }
    public int Version { get; init; }
    public DateTime Timestamp { get; init; }
    public long StateSize { get; init; }
}

/// <summary>
///     Orchestrator configuration
/// </summary>
public record OrchestratorConfiguration
{
    public int PersistBatchSize { get; init; } = 100;
    public TimeSpan PersistInterval { get; init; } = TimeSpan.FromMinutes(5);
    public int MaxStateSize { get; init; } = 2 * 1024 * 1024; // 2MB
    public TimeSpan SafeWindow { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan FallbackCheckInterval { get; init; } = TimeSpan.FromSeconds(30);
    public bool EnableDuplicateCheck { get; init; } = true;
}