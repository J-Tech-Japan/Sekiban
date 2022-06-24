using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.EventSourcing.TestHelpers;

public abstract class SingleAggregateTestBase<TAggregate, TDto> : IDisposable, IAggregateTestHelper<TAggregate, TDto>
    where TAggregate : TransferableAggregateBase<TDto> where TDto : AggregateDtoBase
{
    private readonly AggregateTestHelper<TAggregate, TDto> _helper;
    protected readonly IServiceProvider _serviceProvider;
    public SingleAggregateTestBase()
    {
        // ReSharper disable once VirtualMemberCallInConstructor
        _serviceProvider = SetupService();
        _helper = new AggregateTestHelper<TAggregate, TDto>(_serviceProvider);
    }
    public AggregateTestHelper<TAggregate, TDto> GivenTestResult(Action initialAction) =>
        _helper.GivenTestResult(initialAction);
    public AggregateTestHelper<TAggregate, TDto> GivenEnvironmentDtos(List<AggregateDtoBase> dtos) =>
        _helper.GivenEnvironmentDtos(dtos);
    public AggregateTestHelper<TAggregate, TDto> GivenEnvironmentDto(AggregateDtoBase dto) =>
        _helper.GivenEnvironmentDto(dto);
    public AggregateTestHelper<TAggregate, TDto> Given(TDto snapshot) =>
        _helper.Given(snapshot);
    public AggregateTestHelper<TAggregate, TDto> Given(AggregateEvent ev) =>
        _helper.Given(ev);
    public AggregateTestHelper<TAggregate, TDto> Given(Func<TAggregate, AggregateEvent> evFunc) =>
        _helper.Given(evFunc);
    public AggregateTestHelper<TAggregate, TDto> Given(IEnumerable<AggregateEvent> events) =>
        _helper.Given(events);
    public AggregateTestHelper<TAggregate, TDto> Given(TDto snapshot, AggregateEvent ev) =>
        _helper.Given(snapshot, ev);
    public AggregateTestHelper<TAggregate, TDto> Given(TDto snapshot, IEnumerable<AggregateEvent> ev) =>
        _helper.Given(snapshot, ev);
    public AggregateTestHelper<TAggregate, TDto> WhenCreate<C>(C createCommand) where C : ICreateAggregateCommand<TAggregate> =>
        _helper.WhenCreate(createCommand);
    public AggregateTestHelper<TAggregate, TDto> WhenChange<C>(C changeCommand) where C : ChangeAggregateCommandBase<TAggregate> =>
        _helper.WhenChange(changeCommand);
    public AggregateTestHelper<TAggregate, TDto> WhenChange<C>(Func<TAggregate, C> commandFunc) where C : ChangeAggregateCommandBase<TAggregate> =>
        _helper.WhenChange(commandFunc);
    public AggregateTestHelper<TAggregate, TDto> WhenMethod(Action<TAggregate> action) =>
        _helper.WhenMethod(action);
    public AggregateTestHelper<TAggregate, TDto> WhenConstructor(Func<TAggregate> aggregateFunc) =>
        _helper.WhenConstructor(aggregateFunc);
    public AggregateTestHelper<TAggregate, TDto> ThenEvents(Action<List<AggregateEvent>, TAggregate> checkEventsAction) =>
        _helper.ThenEvents(checkEventsAction);
    public AggregateTestHelper<TAggregate, TDto> ThenEvents(Action<List<AggregateEvent>> checkEventsAction) =>
        _helper.ThenEvents(checkEventsAction);
    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent<T>(Action<T, TAggregate> checkEventAction) where T : AggregateEvent =>
        _helper.ThenSingleEvent(checkEventAction);
    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent<T>(Action<T> checkEventAction) where T : AggregateEvent =>
        _helper.ThenSingleEvent(checkEventAction);
    public AggregateTestHelper<TAggregate, TDto> ThenSingleEvent<T>(Func<TAggregate, T> constructExpectedEvent) where T : AggregateEvent =>
        _helper.ThenSingleEvent(constructExpectedEvent);

    public AggregateTestHelper<TAggregate, TDto> ThenState(Action<TDto, TAggregate> checkDtoAction) =>
        _helper.ThenState(checkDtoAction);
    public AggregateTestHelper<TAggregate, TDto> ThenState(Action<TDto> checkDtoAction) =>
        _helper.ThenState(checkDtoAction);
    public AggregateTestHelper<TAggregate, TDto> ThenState(Func<TAggregate, TDto> constructExpectedDto) =>
        _helper.ThenState(constructExpectedDto);
    public AggregateTestHelper<TAggregate, TDto> ThenThrows<T>() where T : Exception =>
        _helper.ThenThrows<T>();
    public AggregateTestHelper<TAggregate, TDto> ThenThrows<T>(Action<T> checkException) where T : Exception =>
        _helper.ThenThrows(checkException);
    public AggregateTestHelper<TAggregate, TDto> ThenAggregateCheck(Action<TAggregate> checkAction) =>
        _helper.ThenAggregateCheck(checkAction);
    public AggregateTestHelper<TAggregate, TDto> ThenNotThrowsAnException() =>
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
