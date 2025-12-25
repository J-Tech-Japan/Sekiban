using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
namespace Sekiban.Pure.Orleans;

public interface IAggregateProjectorGrain : IGrainWithStringKey
{
    /// <summary>
    ///     Get the current state.
    ///     When the State is not created or the Projector Version has changed,
    ///     consider retrieving events in bulk and rebuilding.
    /// </summary>
    /// <returns>Current aggregate state</returns>
    Task<Aggregate> GetStateAsync();

    /// <summary>
    ///     Entry point for executing commands.
    ///     Generate events using CommandHandler based on the current state and send to AggregateEventHandler.
    /// </summary>
    /// <param name="command">Command to execute</param>
    /// <param name="metadata">Command Metadata</param>
    /// <returns>Return post-execution state and generated events as needed</returns>
    Task<CommandResponse> ExecuteCommandAsync(ICommandWithHandlerSerializable command, CommandMetadata metadata);

    /// <summary>
    ///     Rebuild State from scratch (when Version is upgraded or State is corrupted, etc.).
    ///     Receive all events from AggregateEventHandler and reconstruct through Projector logic.
    /// </summary>
    /// <returns>New state after rebuilding</returns>
    Task<Aggregate> RebuildStateAsync();
}
