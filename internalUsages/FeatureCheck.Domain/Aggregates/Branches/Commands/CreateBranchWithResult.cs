using FeatureCheck.Domain.Aggregates.Branches.Events;
using ResultBoxes;
using Sekiban.Core.Command;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.Branches.Commands;

public record CreateBranchWithResult([property:Required, MaxLength(20)]string Name) : ICommandWithHandler<Branch, CreateBranchWithResult>
{
    public Guid GetAggregateId() => Guid.NewGuid();
    public static ResultBox<UnitValue> HandleCommand(CreateBranchWithResult command, ICommandContext<Branch> context) =>
        context.AppendEvent(new BranchCreated(command.Name));
}