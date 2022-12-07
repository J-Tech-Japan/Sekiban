using FeatureCheck.Domain.Aggregates.Branches.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.Branches.Commands;

public record CreateBranch : ICommand<Branch>, ICleanupNecessaryCommand<CreateBranch>
{
    public CreateBranch() : this(string.Empty)
    {
    }

    public CreateBranch(string name)
    {
        Name = name;
    }

    [Required] [MaxLength(20)] public string Name { get; init; } = string.Empty;

    public CreateBranch CleanupCommandIfNeeded(CreateBranch command)
    {
        return command with { Name = string.Empty };
    }

    public Guid GetAggregateId()
    {
        return Guid.NewGuid();
    }

    public class Handler : ICommandHandler<Branch, CreateBranch>
    {
        public async IAsyncEnumerable<IEventPayload<Branch>> HandleCommandAsync(
            Func<AggregateState<Branch>> getAggregateState,
            CreateBranch command)
        {
            await Task.CompletedTask;
            yield return new BranchCreated(command.Name);
        }
    }
}
