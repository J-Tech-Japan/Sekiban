using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

/// <summary>
///     Interface for defining a command
///     This command is used to optimistically validate command version.
///     ReferenceVersion is checked with current aggregate version,
///     If they are not equal, then command is rejected.
/// </summary>
/// <typeparam name="TAggregatePayload">Target Aggregate</typeparam>
public interface IVersionValidationCommand<TAggregatePayload> : ICommand<TAggregatePayload>, IVersionValidationCommandCommon
    where TAggregatePayload : IAggregatePayloadCommon;
