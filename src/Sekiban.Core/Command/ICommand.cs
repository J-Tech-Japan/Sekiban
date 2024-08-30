using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

/// <summary>
///     Interface for defining a command
/// </summary>
/// <typeparam name="TAggregatePayload">Target Aggregate Payload to execute the command</typeparam>
public interface ICommand<TAggregatePayload> : ICommandCommon<TAggregatePayload> where TAggregatePayload : IAggregatePayloadCommon
{
    /// <summary>
    ///     Set Aggregate Id for the Command.
    ///     To Create new Aggregate, set Guid.NewGuid()
    ///     e.g. public Guid GetAggregateId() => Guid.NewGuid();
    ///     To access already created aggregate, set and use Guid in command.
    ///     e.g. public Guid GetAggregateId() => ClientId;
    /// </summary>
    /// <returns></returns>
    public Guid GetAggregateId();
}

// ReSharper disable once UnusedTypeParameter
[CommandRootPartitionValidation]
// ReSharper disable once UnusedTypeParameter
public interface ICommandCommon<TAggregatePayload> : ICommandCommon where TAggregatePayload : IAggregatePayloadCommon;
