using FeatureCheck.Domain.Aggregates.Branches.Events;
using ResultBoxes;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.Branches.Commands;

public record CreateBranch : ICommand<Branch>, ICleanupNecessaryCommand<CreateBranch>
{

    [Required]
    [MaxLength(20)]
    public string Name { get; init; } = string.Empty;
    public CreateBranch() : this(string.Empty)
    {
    }

    public CreateBranch(string name) => Name = name;

    public CreateBranch CleanupCommand(CreateBranch command) => command with { Name = string.Empty };

    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandler<Branch, CreateBranch>
    {
        public IEnumerable<IEventPayloadApplicableTo<Branch>> HandleCommand(CreateBranch command, ICommandContext<Branch> context)
        {
            yield return new BranchCreated(command.Name);
        }
    }
}

public record CreateBranchWithResult([property:Required, MaxLength(20)]string Name) : ICommandWithStaticHandler<Branch, CreateBranchWithResult>
{
        public Guid GetAggregateId() => Guid.NewGuid();
        public static ResultBox<UnitValue> HandleCommand(CreateBranchWithResult command, ICommandContext<Branch> context) =>
            context.AppendEvent(new BranchCreated(command.Name));
}
