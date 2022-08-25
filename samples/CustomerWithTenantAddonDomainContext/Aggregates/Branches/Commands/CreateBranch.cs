using System.ComponentModel.DataAnnotations;
namespace CustomerWithTenantAddonDomainContext.Aggregates.Branches.Commands;

public record CreateBranch : ICreateAggregateCommand<Branch>
{
    [Required]
    [MaxLength(20)]
    public string Name { get; init; } = string.Empty;
    public CreateBranch() : this(string.Empty) { }
    public CreateBranch(string name) =>
        Name = name;
}
public class CreateBranchHandler : CreateAggregateCommandHandlerBase<Branch, CreateBranch>
{
    public override Guid GenerateAggregateId(CreateBranch command) =>
        Guid.NewGuid();
    protected override async Task ExecCreateCommandAsync(Branch aggregate, CreateBranch command)
    {
        await Task.CompletedTask;
        aggregate.Created(command.Name);
    }
}
