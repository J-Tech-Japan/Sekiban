using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

/// <summary>
///     Interface for defining a command
/// </summary>
/// <typeparam name="TAggregatePayload">Target Aggregate Payload to execute the command</typeparam>
public interface ICommand<TAggregatePayload> : ICommandCommon<TAggregatePayload>
    where TAggregatePayload : IAggregatePayloadCommon
{
}
public interface ICommandCommon<TAggregatePayload> : ICommandCommon where TAggregatePayload : IAggregatePayloadCommon;
