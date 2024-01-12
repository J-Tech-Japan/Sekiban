using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

/// <summary>
///     Interface for defining a command
///     This command is used to optimistically validate command version.
///     ReferenceVersion is checked with current aggregate version,
///     If they are not equal, then command is rejected.
///     If version validation failed, command execution will throw SekibanCommandInconsistentVersionException
/// </summary>
/// <typeparam name="TAggregatePayload">Target Aggregate</typeparam>
public interface ICommandWithVersionValidation<TAggregatePayload> : ICommand<TAggregatePayload>, IVersionValidationCommandCommon
    where TAggregatePayload : IAggregatePayloadCommon;
