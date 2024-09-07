using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Query;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.DeletebleBoxes;

public record Box(string Code, string Name) : IDeletableAggregatePayload<Box>
{
    public static Box CreateInitialPayload(Box? _) => new(string.Empty, string.Empty);
    public bool IsDeleted { get; init; }
}
public record CreateBox(string Code, string Name) : ICommandWithHandler<Box, CreateBox>
{

    public static Guid SpecifyAggregateId(CreateBox command) => Guid.NewGuid();
    public static ResultBox<UnitValue> HandleCommand(CreateBox command, ICommandContext<Box> context) =>
        context.AppendEvent(new BoxCreated(command.Code, command.Name));
}
public record BoxCreated(string Code, string Name) : IEventPayload<Box, BoxCreated>
{
    public static Box OnEvent(Box aggregatePayload, Event<BoxCreated> ev) => aggregatePayload with
    {
        Code = ev.Payload.Code,
        Name = ev.Payload.Name
    };
}
public record BoxDeleted : IEventPayload<Box, BoxDeleted>
{
    public static Box OnEvent(Box aggregatePayload, Event<BoxDeleted> _) => aggregatePayload with
    {
        IsDeleted = true
    };
}
public record DeleteBox(Guid BoxId) : ICommandWithHandlerForExistingAggregate<Box, DeleteBox>
{
    public static ResultBox<UnitValue> HandleCommand(DeleteBox command, ICommandContext<Box> context) =>
        context.AppendEvent(new BoxDeleted());
    public static Guid SpecifyAggregateId(DeleteBox command) => command.BoxId;
}
public record CheckBoxExists(string Code) : INextAggregateQuery<Box, CheckBoxExists, bool>
{
    public static QueryListType GetQueryListType(CheckBoxExists query) => QueryListType.ActiveAndDeleted;
    public static ResultBox<bool> HandleFilter(
        IEnumerable<AggregateState<Box>> list,
        CheckBoxExists query,
        IQueryContext context) => list.Any(x => x.Payload.Code == query.Code);
}
public record CheckBoxExistsOnlyActive(string Code) : INextAggregateQuery<Box, CheckBoxExistsOnlyActive, bool>
{
    public static ResultBox<bool> HandleFilter(
        IEnumerable<AggregateState<Box>> list,
        CheckBoxExistsOnlyActive query,
        IQueryContext context) => list.Any(x => x.Payload.Code == query.Code);
}
