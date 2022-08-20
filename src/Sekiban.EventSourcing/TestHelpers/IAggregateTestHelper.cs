using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.TestHelpers
{
    public interface IAggregateTestHelper<TAggregate, TContents> where TAggregate : TransferableAggregateBase<TContents>
        where TContents : IAggregateContents, new()
    {
        public IAggregateTestHelper<TAggregate, TContents> GivenScenario(Action initialAction);
        public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentDtos(List<ISingleAggregate> dtos);
        public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentDtoContents<DAggregate, DAggregateContents>(
            Guid aggregateId,
            DAggregateContents contents) where DAggregateContents : IAggregateContents, new()
            where DAggregate : TransferableAggregateBase<DAggregateContents>, new();
        public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentDto(ISingleAggregate dto);
        public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot);
        public IAggregateTestHelper<TAggregate, TContents> Given(IAggregateEvent ev);
        public IAggregateTestHelper<TAggregate, TContents> Given(Guid aggregateId, TContents contents);
        public IAggregateTestHelper<TAggregate, TContents> Given<TEventPayload>(Guid aggregateId, TEventPayload payload)
            where TEventPayload : ICreatedEventPayload;
        public IAggregateTestHelper<TAggregate, TContents> Given<TEventPayload>(TEventPayload payload) where TEventPayload : IChangedEventPayload;
        public IAggregateTestHelper<TAggregate, TContents> Given(Func<TAggregate, IAggregateEvent> evFunc);
        public IAggregateTestHelper<TAggregate, TContents> Given(IEnumerable<IAggregateEvent> events);
        public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot, IAggregateEvent ev);
        public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot, IEnumerable<IAggregateEvent> ev);
        public IAggregateTestHelper<TAggregate, TContents> WhenCreate<C>(C createCommand) where C : ICreateAggregateCommand<TAggregate>;
        public IAggregateTestHelper<TAggregate, TContents> WhenChange<C>(C changeCommand) where C : ChangeAggregateCommandBase<TAggregate>;
        public IAggregateTestHelper<TAggregate, TContents> WhenChange<C>(Func<TAggregate, C> commandFunc)
            where C : ChangeAggregateCommandBase<TAggregate>;
        public IAggregateTestHelper<TAggregate, TContents> WhenMethod(Action<TAggregate> action);
        public IAggregateTestHelper<TAggregate, TContents> ThenEvents(Action<List<IAggregateEvent>, TAggregate> checkEventsAction);
        public IAggregateTestHelper<TAggregate, TContents> ThenEvents(Action<List<IAggregateEvent>> checkEventsAction);
        public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Action<T, TAggregate> checkEventAction) where T : IAggregateEvent;
        public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Action<T> checkEventAction) where T : IAggregateEvent;
        public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Func<TAggregate, T> constructExpectedEvent) where T : IAggregateEvent;
        public IAggregateTestHelper<TAggregate, TContents> ThenSingleEventPayload<T>(T payload) where T : IEventPayload;
        public IAggregateTestHelper<TAggregate, TContents> ThenSingleEventPayload<T>(Func<TAggregate, T> constructExpectedEvent) where T : IEventPayload;

        public IAggregateTestHelper<TAggregate, TContents> ThenState(Action<AggregateDto<TContents>, TAggregate> checkDtoAction);
        public IAggregateTestHelper<TAggregate, TContents> ThenState(Action<AggregateDto<TContents>> checkDtoAction);
        public IAggregateTestHelper<TAggregate, TContents> ThenState(Func<TAggregate, AggregateDto<TContents>> constructExpectedDto);
        public IAggregateTestHelper<TAggregate, TContents> ThenContents(TContents contents);
        public IAggregateTestHelper<TAggregate, TContents> ThenContents(Func<TAggregate, TContents> constructExpectedDto);

        public IAggregateTestHelper<TAggregate, TContents> ThenThrows<T>() where T : Exception;
        public IAggregateTestHelper<TAggregate, TContents> ThenThrows<T>(Action<T> checkException) where T : Exception;
        public IAggregateTestHelper<TAggregate, TContents> ThenAggregateCheck(Action<TAggregate> checkAction);
        public IAggregateTestHelper<TAggregate, TContents> ThenNotThrowsAnException();
    }
}
