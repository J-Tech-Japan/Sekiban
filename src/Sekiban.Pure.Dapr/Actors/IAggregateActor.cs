using Dapr.Actors;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;

namespace Sekiban.Pure.Dapr.Actors;

/// <summary>
/// Dapr actor interface for aggregate projection and command execution.
/// This is the Dapr equivalent of Orleans' IAggregateProjectorGrain.
/// </summary>
public interface IAggregateActor : IActor
{
    /// <summary>
    /// Gets the current aggregate state.
    /// If state is not created or projector version has changed,
    /// it will be rebuilt from events.
    /// </summary>
    /// <returns>Current aggregate state</returns>
    Task<Aggregate> GetStateAsync();

    /// <summary>
    /// Entry point for command execution.
    /// Uses the current state and CommandHandler to generate events,
    /// then sends them to AggregateEventHandlerActor.
    /// </summary>
    /// <param name="command">Command to execute</param>
    /// <param name="metadata">Command metadata</param>
    /// <returns>Command response with state and generated events</returns>
    Task<CommandResponse> ExecuteCommandAsync(
        ICommandWithHandlerSerializable command,
        CommandMetadata metadata);

    /// <summary>
    /// Rebuilds state from scratch (for version upgrades or state corruption).
    /// Retrieves all events from AggregateEventHandlerActor and reconstructs
    /// state through the Projector logic.
    /// </summary>
    /// <returns>Newly rebuilt state</returns>
    Task<Aggregate> RebuildStateAsync();
}