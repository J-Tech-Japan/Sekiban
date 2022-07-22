using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.TestHelpers;

public interface IAggregateTestHelper<TAggregate, TContents>
    where TAggregate : TransferableAggregateBase<TContents> where TContents : IAggregateContents
{
    public IAggregateTestHelper<TAggregate, TContents> GivenScenario(Action initialAction);
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentDtos(List<ISingleAggregate> dtos);
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentDto(ISingleAggregate dto);
    public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot);
    public IAggregateTestHelper<TAggregate, TContents> Given(AggregateEvent ev);
    public IAggregateTestHelper<TAggregate, TContents> Given(Func<TAggregate, AggregateEvent> evFunc);
    public IAggregateTestHelper<TAggregate, TContents> Given(IEnumerable<AggregateEvent> events);
    public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot, AggregateEvent ev);
    public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot, IEnumerable<AggregateEvent> ev);
    public IAggregateTestHelper<TAggregate, TContents> WhenCreate<C>(C createCommand) where C : ICreateAggregateCommand<TAggregate>;
    public IAggregateTestHelper<TAggregate, TContents> WhenChange<C>(C changeCommand) where C : ChangeAggregateCommandBase<TAggregate>;
    public IAggregateTestHelper<TAggregate, TContents> WhenChange<C>(Func<TAggregate, C> commandFunc)
        where C : ChangeAggregateCommandBase<TAggregate>;
    public IAggregateTestHelper<TAggregate, TContents> WhenMethod(Action<TAggregate> action);
    public IAggregateTestHelper<TAggregate, TContents> WhenConstructor(Func<TAggregate> aggregateFunc);
    public IAggregateTestHelper<TAggregate, TContents> ThenEvents(Action<List<AggregateEvent>, TAggregate> checkEventsAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenEvents(Action<List<AggregateEvent>> checkEventsAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Action<T, TAggregate> checkEventAction) where T : AggregateEvent;
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Action<T> checkEventAction) where T : AggregateEvent;
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Func<TAggregate, T> constructExpectedEvent) where T : AggregateEvent;

    public IAggregateTestHelper<TAggregate, TContents> ThenState(Action<AggregateDto<TContents>, TAggregate> checkDtoAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenState(Action<AggregateDto<TContents>> checkDtoAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenState(Func<TAggregate, AggregateDto<TContents>> constructExpectedDto);
    public IAggregateTestHelper<TAggregate, TContents> ThenThrows<T>() where T : Exception;
    public IAggregateTestHelper<TAggregate, TContents> ThenThrows<T>(Action<T> checkException) where T : Exception;
    public IAggregateTestHelper<TAggregate, TContents> ThenAggregateCheck(Action<TAggregate> checkAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenNotThrowsAnException();
}
