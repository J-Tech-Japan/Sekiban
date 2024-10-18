using FeatureCheck.Domain.Aggregates.Branches.Events;
using ResultBoxes;
using Sekiban.Core.Command;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.Branches.Commands;

public record CreateBranchWithResult(
    [property: Required]
    [property: MaxLength(20)]
    string Name) : ICommandWithHandler<Branch, CreateBranchWithResult>
{
    public static ResultBox<EventOrNone<Branch>> HandleCommand(
        CreateBranchWithResult command,
        ICommandContext<Branch> context) =>
        context.AppendEvent(new BranchCreated(command.Name));
    public static Guid SpecifyAggregateId(CreateBranchWithResult command) => Guid.NewGuid();
}
