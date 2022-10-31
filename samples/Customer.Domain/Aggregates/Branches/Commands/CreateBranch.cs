using Customer.Domain.Aggregates.Branches.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using System.ComponentModel.DataAnnotations;
namespace Customer.Domain.Aggregates.Branches.Commands;

public record CreateBranch : ICreateCommand<Branch>
{
    public CreateBranch() : this(string.Empty) { }
    public CreateBranch(string name) => Name = name;
    [Required]
    [MaxLength(20)]
    public string Name { get; init; } = string.Empty;
    public Guid GetAggregateId() => Guid.NewGuid();
}
public class CreateBranchHandler : CreateCommandHandlerBase<Branch, CreateBranch>
{
    public override CreateBranch CleanupCommandIfNeeded(CreateBranch command) => command with { Name = string.Empty };
    protected override async IAsyncEnumerable<IApplicableEvent<Branch>> ExecCreateCommandAsync(
        Func<AggregateState<Branch>> getAggregateState,
        CreateBranch command)
    {
        await Task.CompletedTask;
        yield return new BranchCreated(command.Name);
    }
}
