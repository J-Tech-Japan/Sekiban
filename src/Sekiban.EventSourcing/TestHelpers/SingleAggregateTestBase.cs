using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.TestHelpers;

public abstract class SingleAggregateTestBase<TAggregate, TContents> : IDisposable, IAggregateTestHelper<TAggregate, TContents>
    where TAggregate : TransferableAggregateBase<TContents> where TContents : IAggregateContents
{
    private readonly IAggregateTestHelper<TAggregate, TContents> _helper;
    protected readonly IServiceProvider _serviceProvider;
    public SingleAggregateTestBase()
    {
        // ReSharper disable once VirtualMemberCallInConstructor
        _serviceProvider = SetupService();
        _helper = new AggregateTestHelper<TAggregate, TContents>(_serviceProvider);
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenScenario(Action initialAction) =>
        _helper.GivenScenario(initialAction);
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentDtos(List<ISingleAggregate> dtos) =>
        _helper.GivenEnvironmentDtos(dtos);
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentDto(ISingleAggregate dto) =>
        _helper.GivenEnvironmentDto(dto);
    public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot) =>
        _helper.Given(snapshot);
    public IAggregateTestHelper<TAggregate, TContents> Given(IAggregateEvent ev) =>
        _helper.Given(ev);
    public IAggregateTestHelper<TAggregate, TContents> Given<TEventPayload>(Guid aggregateId, TEventPayload payload)
        where TEventPayload : ICreatedEventPayload =>
        _helper.Given(aggregateId, payload);
    public IAggregateTestHelper<TAggregate, TContents> Given<TEventPayload>(TEventPayload payload) where TEventPayload : IChangedEventPayload =>
        _helper.Given(payload);
    public IAggregateTestHelper<TAggregate, TContents> Given(Func<TAggregate, IAggregateEvent> evFunc) =>
        _helper.Given(evFunc);
    public IAggregateTestHelper<TAggregate, TContents> Given(IEnumerable<IAggregateEvent> events) =>
        _helper.Given(events);
    public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot, IAggregateEvent ev) =>
        _helper.Given(snapshot, ev);
    public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot, IEnumerable<IAggregateEvent> ev) =>
        _helper.Given(snapshot, ev);
    public IAggregateTestHelper<TAggregate, TContents> WhenCreate<C>(C createCommand) where C : ICreateAggregateCommand<TAggregate> =>
        _helper.WhenCreate(createCommand);
    public IAggregateTestHelper<TAggregate, TContents> WhenChange<C>(C changeCommand) where C : ChangeAggregateCommandBase<TAggregate> =>
        _helper.WhenChange(changeCommand);
    public IAggregateTestHelper<TAggregate, TContents> WhenChange<C>(Func<TAggregate, C> commandFunc)
        where C : ChangeAggregateCommandBase<TAggregate> =>
        _helper.WhenChange(commandFunc);
    public IAggregateTestHelper<TAggregate, TContents> WhenMethod(Action<TAggregate> action) =>
        _helper.WhenMethod(action);
    public IAggregateTestHelper<TAggregate, TContents> WhenConstructor(Func<TAggregate> aggregateFunc) =>
        _helper.WhenConstructor(aggregateFunc);
    public IAggregateTestHelper<TAggregate, TContents> ThenEvents(Action<List<IAggregateEvent>, TAggregate> checkEventsAction) =>
        _helper.ThenEvents(checkEventsAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenEvents(Action<List<IAggregateEvent>> checkEventsAction) =>
        _helper.ThenEvents(checkEventsAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Action<T, TAggregate> checkEventAction) where T : IAggregateEvent =>
        _helper.ThenSingleEvent(checkEventAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Action<T> checkEventAction) where T : IAggregateEvent =>
        _helper.ThenSingleEvent(checkEventAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Func<TAggregate, T> constructExpectedEvent) where T : IAggregateEvent =>
        _helper.ThenSingleEvent(constructExpectedEvent);
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEventPayload<T>(T payload) where T : IEventPayload =>
        _helper.ThenSingleEventPayload(payload);

    public IAggregateTestHelper<TAggregate, TContents> ThenState(Action<AggregateDto<TContents>, TAggregate> checkDtoAction) =>
        _helper.ThenState(checkDtoAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenState(Action<AggregateDto<TContents>> checkDtoAction) =>
        _helper.ThenState(checkDtoAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenState(Func<TAggregate, AggregateDto<TContents>> constructExpectedDto) =>
        _helper.ThenState(constructExpectedDto);
    public IAggregateTestHelper<TAggregate, TContents> ThenThrows<T>() where T : Exception =>
        _helper.ThenThrows<T>();
    public IAggregateTestHelper<TAggregate, TContents> ThenThrows<T>(Action<T> checkException) where T : Exception =>
        _helper.ThenThrows(checkException);
    public IAggregateTestHelper<TAggregate, TContents> ThenAggregateCheck(Action<TAggregate> checkAction) =>
        _helper.ThenAggregateCheck(checkAction);
    public IAggregateTestHelper<TAggregate, TContents> ThenNotThrowsAnException() =>
        _helper.ThenNotThrowsAnException();
    public void Dispose() { }
    public abstract IServiceProvider SetupService();
    public T GetService<T>()
    {
        var toreturn = _serviceProvider.GetService<T>();
        if (toreturn == null)
        {
            throw new Exception("オブジェクトが登録されていません。" + typeof(T));
        }
        return toreturn;
    }
}
