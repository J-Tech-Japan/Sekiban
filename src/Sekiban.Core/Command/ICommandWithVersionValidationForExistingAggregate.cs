using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICommandWithVersionValidationForExistingAggregate<TAggregatePayload> : ICommand<TAggregatePayload>, IVersionValidationCommandCommon,
    IAggregateShouldExistCommand where TAggregatePayload : IAggregatePayloadCommon;
