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
        public async IAsyncEnumerable<IEventPayloadApplicableTo<Branch>> HandleCommandAsync(
            Func<AggregateState<Branch>> getAggregateState,
            CreateBranchWithRootPartitionKey command)
        {
            await Task.CompletedTask;
            yield return new BranchCreated(command.Name);
        }
    }
}
