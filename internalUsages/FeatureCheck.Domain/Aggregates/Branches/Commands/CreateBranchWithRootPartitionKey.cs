using FeatureCheck.Domain.Aggregates.Branches.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.Branches.Commands;

public class CreateBranchWithRootPartitionKey : ICommand<Branch>
{
    public string RootPartitionKey { get; init; } = string.Empty;

    [Required]
    public string Name { get; init; } = string.Empty;

    public string GetRootPartitionKey() => RootPartitionKey;

    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandler<Branch, CreateBranchWithRootPartitionKey>
    {
        public IEnumerable<IEventPayloadApplicableTo<Branch>> HandleCommand(
            CreateBranchWithRootPartitionKey command,
            ICommandContext<Branch> context)
        {
            yield return new BranchCreated(command.Name);
        }
        public Guid SpecifyAggregateId(CreateBranchWithRootPartitionKey command) => Guid.NewGuid();
    }
}
