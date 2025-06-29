using Dapr.Actors;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;

namespace Sekiban.Pure.Dapr.Actors;

/// <summary>
/// Dapr actor interface for aggregate projection and command execution.
/// This is the Dapr equivalent of Orleans' IAggregateProjectorGrain.
/// Uses concrete types for proper JSON serialization by Dapr.
/// </summary>
public interface IAggregateActor : IActor
{
    /// <summary>
    /// Gets the current aggregate state.
    /// If state is not created or projector version has changed,
    /// it will be rebuilt from events.
    /// </summary>
    /// <returns>Current aggregate state as a serializable aggregate</returns>
    Task<SerializableAggregate> GetAggregateStateAsync();

    /// <summary>
    /// Entry point for command execution.
    /// Accepts a SerializableCommandAndMetadata containing the command and metadata.
    /// Uses the current state and CommandHandler to generate events,
    /// then sends them to AggregateEventHandlerActor.
    /// </summary>
    /// <param name="commandAndMetadata">Serializable command and metadata</param>
    /// <returns>Command response with state and generated events</returns>
    Task<string> ExecuteCommandAsync(SerializableCommandAndMetadata commandAndMetadata);

    /// <summary>
    /// Rebuilds state from scratch (for version upgrades or state corruption).
    /// Retrieves all events from AggregateEventHandlerActor and reconstructs
    /// state through the Projector logic.
    /// </summary>
    /// <returns>Newly rebuilt state as a serializable aggregate</returns>
    Task<SerializableAggregate> RebuildStateAsync();
}
