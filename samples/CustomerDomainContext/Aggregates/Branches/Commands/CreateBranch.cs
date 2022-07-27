namespace CustomerDomainContext.Aggregates.Branches.Commands;

public record CreateBranch(string Name) : ICreateAggregateCommand<Branch>;
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
