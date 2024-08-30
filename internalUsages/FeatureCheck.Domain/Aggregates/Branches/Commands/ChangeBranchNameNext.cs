using FeatureCheck.Domain.Aggregates.Branches.Events;
using ResultBoxes;
using Sekiban.Core.Command;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.Branches.Commands;

public record ChangeBranchNameNext(
    Guid BranchId,
    [property: Required]
    [property: MaxLength(20)]
    string Name) : ICommandWithHandlerForExistingAggregate<Branch, ChangeBranchNameNext>
{
    public static ResultBox<UnitValue> HandleCommand(ChangeBranchNameNext command, ICommandContext<Branch> context) =>
        context.AppendEvent(new BranchNameChanged(command.Name));
    public static Guid SpecifyAggregateId(ChangeBranchNameNext command) => command.BranchId;
}
