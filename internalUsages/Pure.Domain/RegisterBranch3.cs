using Sekiban.Pure;
using Sekiban.Pure.Command.Handlers;
namespace Pure.Domain;

public record RegisterBranch3(string Name) : ICommandWithAggregateRestriction<EmptyAggregatePayload>
{
}
