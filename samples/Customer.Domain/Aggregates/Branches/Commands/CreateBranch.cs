using Customer.Domain.Aggregates.Branches.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using System.ComponentModel.DataAnnotations;
namespace Customer.Domain.Aggregates.Branches.Commands;

public record CreateBranch : ICreateAggregateCommand<Branch>
{
    public CreateBranch() : this(string.Empty) { }
    public CreateBranch(string name)
    {
        Name = name;
    }
    [Required]
    [MaxLength(20)]
    public string Name { get; init; } = string.Empty;
    public Guid GetAggregateId()
    {
        return Guid.NewGuid();
    }
}
public class CreateBranchHandler : CreateAggregateCommandHandlerBase<Branch, CreateBranch>
{
    public override CreateBranch CleanupCommandIfNeeded(CreateBranch command)
    {
        return command with { Name = string.Empty };
    }
    protected override async IAsyncEnumerable<IApplicableEvent<Branch>> ExecCreateCommandAsync(
        AggregateState<Branch> aggregate,
        CreateBranch command)
    {
        await Task.CompletedTask;
        yield return new BranchCreated(command.Name);
    }
}
