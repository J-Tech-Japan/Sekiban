using Dapr.Actors;

namespace Sekiban.Pure.Dapr.Actors;

/// <summary>
/// Protobuf-enabled Dapr actor interface for aggregate projection and command execution.
/// All methods accept and return byte arrays containing serialized Protobuf messages.
/// </summary>
public interface IProtobufAggregateActor : IActor
{
    /// <summary>
    /// Gets the current aggregate state as a Protobuf-serialized byte array.
    /// Returns a ProtobufAggregateEnvelope message.
    /// </summary>
    /// <returns>Protobuf-serialized aggregate state</returns>
    Task<byte[]> GetStateAsync();

    /// <summary>
    /// Executes a command and returns the response as a Protobuf-serialized byte array.
    /// Accepts an ExecuteCommandRequest message and returns a ProtobufCommandResponse message.
    /// </summary>
    /// <param name="commandData">Protobuf-serialized ExecuteCommandRequest</param>
    /// <returns>Protobuf-serialized ProtobufCommandResponse</returns>
    Task<byte[]> ExecuteCommandAsync(byte[] commandData);

    /// <summary>
    /// Rebuilds state from scratch and returns it as a Protobuf-serialized byte array.
    /// Returns a ProtobufAggregateEnvelope message.
    /// </summary>
    /// <returns>Protobuf-serialized rebuilt aggregate state</returns>
    Task<byte[]> RebuildStateAsync();
}