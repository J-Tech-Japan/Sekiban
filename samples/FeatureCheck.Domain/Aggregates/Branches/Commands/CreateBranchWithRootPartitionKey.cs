using FeatureCheck.Domain.Aggregates.Branches.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.Branches.Commands;

public class CreateBranchWithRootPartitionKey : ICommand<Branch>
{
    public string RootPartitionKey { get; init; } = string.Empty;
    [Required]
    public string Name { get; init; } = string.Empty;
    public Guid GetAggregateId() => Guid.NewGuid();
    public string GetRootPartitionKey() => RootPartitionKey;

    public class Handler : ICommandHandler<Branch, CreateBranchWithRootPartitionKey>
    {
        public IEnumerable<IEventPayloadApplicableTo<Branch>> HandleCommand(
            Func<AggregateState<Branch>> getAggregateState,
            CreateBranchWithRootPartitionKey command)
        {
            yield return new BranchCreated(command.Name);
        }
    }
}
