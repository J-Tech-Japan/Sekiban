namespace CustomerDomainContext.Aggregates.Branches.Commands;

public record CreateBranch(string Name) : ICreateAggregateCommand<Branch>;
public class CreateBranchHandler : CreateAggregateCommandHandlerBase<Branch, CreateBranch>
{
    protected override async Task<Branch> CreateAggregateAsync(CreateBranch command)
    {
        await Task.CompletedTask;
        return new Branch(command.Name);
    }
}
