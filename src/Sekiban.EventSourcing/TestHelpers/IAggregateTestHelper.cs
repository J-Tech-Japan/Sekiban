namespace Sekiban.EventSourcing.TestHelpers;

public interface IAggregateTestHelper<TAggregate, TDto> where TAggregate : TransferableAggregateBase<TDto> where TDto : AggregateDtoBase
{
    public AggregateTestHelper<TAggregate, TDto> GivenEnvironmentDtos(List<AggregateDtoBase> dtos);
    public AggregateTestHelper<TAggregate, TDto> Given(TDto snapshot);
    public AggregateTestHelper<TAggregate, TDto> Given(AggregateEvent ev);
    public AggregateTestHelper<TAggregate, TDto> Given(Func<TAggregate, AggregateEvent> evFunc);
    public AggregateTestHelper<TAggregate, TDto> Given(IEnumerable<AggregateEvent> events);
    public AggregateTestHelper<TAggregate, TDto> Given(TDto snapshot, AggregateEvent ev);
    public AggregateTestHelper<TAggregate, TDto> Given(TDto snapshot, IEnumerable<AggregateEvent> ev);
    public AggregateTestHelper<TAggregate, TDto> WhenCreate<C>(C createCommand) where C : ICreateAggregateCommand<TAggregate>;
    public AggregateTestHelper<TAggregate, TDto> WhenChange<C>(C changeCommand) where C : ChangeAggregateCommandBase<TAggregate>;
    public AggregateTestHelper<TAggregate, TDto> WhenChange<C>(Func<TAggregate, C> commandFunc) where C : ChangeAggregateCommandBase<TAggregate>;
    public AggregateTestHelper<TAggregate, TDto> WhenMethod(Action<TAggregate> action);
    public AggregateTestHelper<TAggregate, TDto> WhenConstructor(Func<TAggregate> aggregateFunc);
    public AggregateTestHelper<TAggregate, TDto> ThenEvents(Action<List<AggregateEvent>, TAggregate> checkEventsAction);
    public AggregateTestHelper<TAggregate, TDto> ThenEvents(Action<List<AggregateEvent>> checkEventsAction);
    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent(Action<AggregateEvent, TAggregate> checkEventAction);
    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent(Action<AggregateEvent> checkEventAction);
    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent<T>(Action<T, TAggregate> checkEventAction) where T : AggregateEvent;
    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent<T>(Action<T> checkEventAction) where T : AggregateEvent;
    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent<T>(Func<TAggregate, T> constructExpectedEvent) where T : AggregateEvent;

    public AggregateTestHelper<TAggregate, TDto> Expect(Action<TDto, TAggregate> checkDtoAction);
    public AggregateTestHelper<TAggregate, TDto> Expect(Action<TDto> checkDtoAction);
    public AggregateTestHelper<TAggregate, TDto> Expect(Func<TAggregate, TDto> constructExpectedDto);
    public AggregateTestHelper<TAggregate, TDto> ShouldThrows<T>() where T : Exception;
}
