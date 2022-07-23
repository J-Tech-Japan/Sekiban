namespace CustomerDomainContext.Aggregates.Branches.Commands;

public record CreateBranch(Guid branchId, string Name) : ICreateAggregateCommand<Branch>;
public class CreateBranchHandler : CreateAggregateCommandHandlerBase<Branch, CreateBranch>
{
    protected override async Task ExecCreateCommandAsync(Branch aggregate, CreateBranch command)
    {
        await Task.CompletedTask;
        aggregate.Created(command.Name);
    }
}
