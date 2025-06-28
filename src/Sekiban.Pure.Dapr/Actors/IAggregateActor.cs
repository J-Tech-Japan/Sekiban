using Dapr.Actors;
using Sekiban.Pure.Aggregates;

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
    /// <returns>Current aggregate state as JSON string</returns>
    Task<string> GetStateAsync();

    /// <summary>
    /// Entry point for command execution.
    /// Accepts a CommandEnvelope containing Protobuf-serialized command.
    /// Uses the current state and CommandHandler to generate events,
    /// then sends them to AggregateEventHandlerActor.
    /// </summary>
    /// <param name="envelope">Command envelope with Protobuf payload</param>
    /// <returns>Command response with state and generated events</returns>
    Task<CommandResponse> ExecuteCommandAsync(CommandEnvelope envelope);

    /// <summary>
    /// Rebuilds state from scratch (for version upgrades or state corruption).
    /// Retrieves all events from AggregateEventHandlerActor and reconstructs
    /// state through the Projector logic.
    /// </summary>
    /// <returns>Newly rebuilt state as an envelope with JSON→Binary payload</returns>
    Task<AggregateEnvelope> RebuildStateAsync();
}
