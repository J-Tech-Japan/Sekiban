using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Orchestrates projection processing with complete testability
///     Manages event processing pipeline independent of Orleans/streaming implementation
/// </summary>
public interface IProjectionOrchestrator
{
    /// <summary>
    ///     Initialize the orchestrator with optional persisted state
    /// </summary>
    Task<ResultBox<ProjectionState>> InitializeAsync(
        string projectorName,
        SerializedProjectionState? persistedState = null);

    /// <summary>
    ///     Process a batch of events with specified context
    /// </summary>
    Task<ResultBox<ProcessResult>> ProcessEventsAsync(
        IReadOnlyList<Event> events,
        ProcessingContext context);

    /// <summary>
    ///     Process a single streaming event
    /// </summary>
    Task<ResultBox<ProcessResult>> ProcessStreamEventAsync(
        Event evt,
        StreamContext context);

    /// <summary>
    ///     Get current projection state
    /// </summary>
    Task<ResultBox<ProjectionState>> GetCurrentStateAsync();

    /// <summary>
    ///     Serialize current state for persistence
    /// </summary>
    Task<ResultBox<SerializedProjectionState>> SerializeStateAsync();

    /// <summary>
    ///     Restore from persisted state
    /// </summary>
    Task<ResultBox<bool>> RestoreStateAsync(SerializedProjectionState state);

    /// <summary>
    ///     Get current position information
    /// </summary>
    Task<ResultBox<PositionInfo>> GetPositionInfoAsync();

    /// <summary>
    ///     Update position tracking
    /// </summary>
    Task<ResultBox<bool>> UpdatePositionAsync(string position, bool isSafe);

    /// <summary>
    ///     Check if an event has been processed
    /// </summary>
    Task<bool> IsEventProcessedAsync(string sortableUniqueId);

    /// <summary>
    ///     Load events from event store for catch-up
    /// </summary>
    Task<ResultBox<IReadOnlyList<Event>>> LoadEventsFromStoreAsync(
        string? fromPosition = null,
        int batchSize = 1000);
}

/// <summary>
///     Context for processing events
/// </summary>
public record ProcessingContext(
    bool IsStreaming,           // true: streaming mode, false: catch-up mode
    bool CheckDuplicates,       // Whether to check for duplicate events
    int BatchSize,              // Batch size for processing
    TimeSpan SafeWindow);       // Safe window duration for determining safe events

/// <summary>
///     Context for streaming events
/// </summary>
public record StreamContext(
    bool IsLive,                // Whether this is a live event
    string? StreamId);          // Optional stream identifier

/// <summary>
///     Result of event processing
/// </summary>
public record ProcessResult(
    int ProcessedCount,         // Number of events processed
    string? LastPosition,       // Last processed position
    string? SafePosition,       // Last safe position
    bool RequiresPersist,       // Whether persistence is required
    TimeSpan ProcessingTime);   // Time taken to process

/// <summary>
///     Position tracking information
/// </summary>
public record PositionInfo(
    string? LastPosition,       // Last processed event position
    string? SafePosition,       // Last safe event position  
    long EventsProcessed);      // Total events processed

/// <summary>
///     Serialized projection state for persistence
/// </summary>
public record SerializedProjectionState(
    byte[] Payload,             // Serialized projection payload
    string TypeName,            // Type name of the projection
    string ProjectorName,       // Name of the projector
    string Version,             // Version of the projection
    PositionInfo Position,      // Position information
    DateTime Timestamp,         // Timestamp of serialization
    bool IsCaughtUp);          // Whether projection has caught up

/// <summary>
///     Current projection state
/// </summary>
public record ProjectionState(
    string ProjectorName,       // Name of the projector
    IMultiProjectionPayload? Payload,  // Current projection payload
    PositionInfo Position,      // Position information
    bool IsCaughtUp);          // Whether caught up to live events