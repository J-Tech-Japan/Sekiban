using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes;

public interface IInheritedAggregate : IParentAggregatePayload<IInheritedAggregate, ProcessingSubAggregate>
{
}
public class ProcessingSubAggregate : IAggregateSubtypePayload<IInheritedAggregate>, IInheritedAggregate
{
    public int YearMonth { get; init; }
}
public class ClosedSubAggregate : ProcessingSubAggregate
{
}
public record InheritedAggregateOpened(int YearMonth) : IEventPayload<ProcessingSubAggregate, InheritedAggregateOpened>
{
    public ProcessingSubAggregate OnEventInstance(ProcessingSubAggregate aggregatePayload, Event<InheritedAggregateOpened> ev) =>
        OnEvent(aggregatePayload, ev);
    public static ProcessingSubAggregate OnEvent(ProcessingSubAggregate aggregatePayload, Event<InheritedAggregateOpened> ev) => new()
        { YearMonth = ev.Payload.YearMonth };
}
public record InheritedAggregateClosed(string Reason) : IEventPayload<ProcessingSubAggregate, ClosedSubAggregate, InheritedAggregateClosed>
{
    public ClosedSubAggregate OnEventInstance(ProcessingSubAggregate aggregatePayload, Event<InheritedAggregateClosed> ev) =>
        OnEvent(aggregatePayload, ev);
    public static ClosedSubAggregate OnEvent(ProcessingSubAggregate aggregatePayload, Event<InheritedAggregateClosed> ev) => new()
        { YearMonth = aggregatePayload.YearMonth };
}
public record InheritedAggregateReopened(string Reason) : IEventPayload<ClosedSubAggregate, ProcessingSubAggregate, InheritedAggregateReopened>
{
    public ProcessingSubAggregate OnEventInstance(ClosedSubAggregate aggregatePayload, Event<InheritedAggregateReopened> ev) =>
        OnEvent(aggregatePayload, ev);
    public static ProcessingSubAggregate OnEvent(ClosedSubAggregate aggregatePayload, Event<InheritedAggregateReopened> ev) => aggregatePayload;
}
public record OpenInheritedAggregate : ICommand<ProcessingSubAggregate>
{
    [Range(202001, 203012)]
    public int YearMonth { get; init; }
    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandler<ProcessingSubAggregate, OpenInheritedAggregate>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<ProcessingSubAggregate>> HandleCommandAsync(
            Func<AggregateState<ProcessingSubAggregate>> getAggregateState,
            OpenInheritedAggregate command)
        {
            await Task.CompletedTask;
            yield return new InheritedAggregateOpened(command.YearMonth);
        }
    }
}
public record CloseInheritedAggregate : ICommand<ProcessingSubAggregate>
{
    public string Reason { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }
    public Guid GetAggregateId() => AggregateId;

    public class Handler : ICommandHandler<ProcessingSubAggregate, CloseInheritedAggregate>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<ProcessingSubAggregate>> HandleCommandAsync(
            Func<AggregateState<ProcessingSubAggregate>> getAggregateState,
            CloseInheritedAggregate command)
        {
            await Task.CompletedTask;
            yield return new InheritedAggregateClosed(command.Reason);
        }
    }
}
public record ReopenInheritedAggregate : ICommand<ClosedSubAggregate>
{
    public string Reason { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }
    public Guid GetAggregateId() => AggregateId;

    public class Handler : ICommandHandler<ClosedSubAggregate, ReopenInheritedAggregate>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<ClosedSubAggregate>> HandleCommandAsync(
            Func<AggregateState<ClosedSubAggregate>> getAggregateState,
            ReopenInheritedAggregate command)
        {
            await Task.CompletedTask;
            yield return new InheritedAggregateReopened(command.Reason);
        }
    }
}
