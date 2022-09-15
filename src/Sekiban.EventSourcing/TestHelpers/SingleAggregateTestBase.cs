using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Shared;
using Sekiban.EventSourcing.Validations;
namespace Sekiban.EventSourcing.TestHelpers;

public abstract class SingleAggregateTestBase<TAggregate, TContents> : IDisposable, IAggregateTestHelper<TAggregate, TContents>
    where TAggregate : TransferableAggregateBase<TContents>, new() where TContents : IAggregateContents, new()
{
    private readonly IAggregateTestHelper<TAggregate, TContents> _helper;
    protected readonly IServiceProvider _serviceProvider;
    public SingleAggregateTestBase(SekibanDependencyOptions dependencyOptions)
    {
        var services = new ServiceCollection();
        SetupDependency(services);
        SekibanEventSourcingDependency.RegisterForAggregateTest(services, dependencyOptions);
        _serviceProvider = services.BuildServiceProvider();
        _helper = new AggregateTestHelper<TAggregate, TContents>(_serviceProvider);
    }

    public IAggregateTestHelper<TAggregate, TContents> GivenScenario(Action initialAction)
    {
        return _helper.GivenScenario(initialAction);
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentDtos(List<ISingleAggregate> dtos)
    {
        return _helper.GivenEnvironmentDtos(dtos);
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentDtoContents<DAggregate, DAggregateContents>(
        Guid aggregateId,
        DAggregateContents contents) where DAggregateContents : IAggregateContents, new()
        where DAggregate : TransferableAggregateBase<DAggregateContents>, new()
    {
        return _helper.GivenEnvironmentDtoContents<DAggregate, DAggregateContents>(aggregateId, contents);
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentDto(ISingleAggregate dto)
    {
        return _helper.GivenEnvironmentDto(dto);
    }
    public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot)
    {
        return _helper.Given(snapshot);
    }
    public IAggregateTestHelper<TAggregate, TContents> Given(IAggregateEvent ev)
    {
        return _helper.Given(ev);
    }
    public IAggregateTestHelper<TAggregate, TContents> Given(Guid aggregateId, TContents contents)
    {
        return _helper.Given(aggregateId, contents);
    }
    public IAggregateTestHelper<TAggregate, TContents> Given<TEventPayload>(Guid aggregateId, TEventPayload payload)
        where TEventPayload : ICreatedEventPayload
    {
        return _helper.Given(aggregateId, payload);
    }
    public IAggregateTestHelper<TAggregate, TContents> Given<TEventPayload>(TEventPayload payload) where TEventPayload : IChangedEventPayload
    {
        return _helper.Given(payload);
    }
    public IAggregateTestHelper<TAggregate, TContents> Given(Func<TAggregate, IAggregateEvent> evFunc)
    {
        return _helper.Given(evFunc);
    }
    public IAggregateTestHelper<TAggregate, TContents> Given(IEnumerable<IAggregateEvent> events)
    {
        return _helper.Given(events);
    }
    public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot, IAggregateEvent ev)
    {
        return _helper.Given(snapshot, ev);
    }
    public IAggregateTestHelper<TAggregate, TContents> Given(AggregateDto<TContents> snapshot, IEnumerable<IAggregateEvent> ev)
    {
        return _helper.Given(snapshot, ev);
    }
    public IAggregateTestHelper<TAggregate, TContents> WhenCreate<C>(C createCommand) where C : ICreateAggregateCommand<TAggregate>
    {
        return _helper.WhenCreate(createCommand);
    }
    public IAggregateTestHelper<TAggregate, TContents> WhenChange<C>(C changeCommand) where C : ChangeAggregateCommandBase<TAggregate>
    {
        return _helper.WhenChange(changeCommand);
    }
    public IAggregateTestHelper<TAggregate, TContents> WhenChange<C>(Func<TAggregate, C> commandFunc) where C : ChangeAggregateCommandBase<TAggregate>
    {
        return _helper.WhenChange(commandFunc);
    }
    public IAggregateTestHelper<TAggregate, TContents> WhenMethod(Action<TAggregate> action)
    {
        return _helper.WhenMethod(action);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenEvents(Action<List<IAggregateEvent>, TAggregate> checkEventsAction)
    {
        return _helper.ThenEvents(checkEventsAction);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenEvents(Action<List<IAggregateEvent>> checkEventsAction)
    {
        return _helper.ThenEvents(checkEventsAction);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Action<T, TAggregate> checkEventAction) where T : IAggregateEvent
    {
        return _helper.ThenSingleEvent(checkEventAction);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Action<T> checkEventAction) where T : IAggregateEvent
    {
        return _helper.ThenSingleEvent(checkEventAction);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEvent<T>(Func<TAggregate, T> constructExpectedEvent) where T : IAggregateEvent
    {
        return _helper.ThenSingleEvent(constructExpectedEvent);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEventPayload<T>(T payload) where T : IEventPayload
    {
        return _helper.ThenSingleEventPayload(payload);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEventPayload<T>(Func<TAggregate, T> constructExpectedEvent) where T : IEventPayload
    {
        return _helper.ThenSingleEventPayload(constructExpectedEvent);
    }

    public IAggregateTestHelper<TAggregate, TContents> ThenState(Action<AggregateDto<TContents>, TAggregate> checkDtoAction)
    {
        return _helper.ThenState(checkDtoAction);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenState(Action<AggregateDto<TContents>> checkDtoAction)
    {
        return _helper.ThenState(checkDtoAction);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenState(Func<TAggregate, AggregateDto<TContents>> constructExpectedDto)
    {
        return _helper.ThenState(constructExpectedDto);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenContents(TContents contents)
    {
        return _helper.ThenContents(contents);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenContents(Func<TAggregate, TContents> constructExpectedDto)
    {
        return _helper.ThenContents(constructExpectedDto);
    }

    public IAggregateTestHelper<TAggregate, TContents> ThenThrows<T>() where T : Exception
    {
        return _helper.ThenThrows<T>();
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenThrows<T>(Action<T> checkException) where T : Exception
    {
        return _helper.ThenThrows(checkException);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenAggregateCheck(Action<TAggregate> checkAction)
    {
        return _helper.ThenAggregateCheck(checkAction);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenNotThrowsAnException()
    {
        return _helper.ThenNotThrowsAnException();
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenHasValidationErrors(IEnumerable<SekibanValidationParameterError> validationParameterErrors)
    {
        return _helper.ThenHasValidationErrors(validationParameterErrors);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenHasValidationErrors()
    {
        return _helper.ThenHasValidationErrors();
    }
    public void Dispose() { }

    protected void SetupDependency(IServiceCollection serviceCollection)
    {

    }
    public T GetService<T>()
    {
        var toreturn = _serviceProvider.GetService<T>();
        if (toreturn is null)
        {
            throw new Exception("オブジェクトが登録されていません。" + typeof(T));
        }
        return toreturn;
    }
}
