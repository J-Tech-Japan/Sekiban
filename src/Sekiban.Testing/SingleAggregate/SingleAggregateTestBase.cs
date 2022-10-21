using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Event;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
namespace Sekiban.Testing.SingleAggregate;

public abstract class SingleAggregateTestBase<TAggregate, TContents, TDependencyDefinition> : IDisposable, IAggregateTestHelper<TAggregate, TContents>
    where TAggregate : AggregateBase<TContents>, new()
    where TContents : IAggregateContents, new()
    where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly IAggregateTestHelper<TAggregate, TContents> _helper;
    protected readonly IServiceProvider _serviceProvider;
    protected SingleAggregateTestBase()
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        services.AddQueryFiltersFromDependencyDefinition(new TDependencyDefinition());
        services.AddSekibanCoreForAggregateTestWithDependency(new TDependencyDefinition());
        _serviceProvider = services.BuildServiceProvider();
        _helper = new AggregateTestHelper<TAggregate, TContents>(_serviceProvider);
    }
    public TSingleAggregateProjection SetupSingleAggregateProjection<TSingleAggregateProjection>()
        where TSingleAggregateProjection : SingleAggregateTestBase
    {
        return _helper.SetupSingleAggregateProjection<TSingleAggregateProjection>();
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenScenario(Action initialAction)
    {
        return _helper.GivenScenario(initialAction);
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentEvent(IAggregateEvent ev)
    {
        return _helper.GivenEnvironmentEvent(ev);
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentEvents(IEnumerable<IAggregateEvent> events)
    {
        return _helper.GivenEnvironmentEvents(events);
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentEventsFile(string filename)
    {
        return _helper.GivenEnvironmentEventsFile(filename);
    }
    public AggregateDto<TEnvironmentAggregateContents>
        GetEnvironmentAggregateDto<TEnvironmentAggregate, TEnvironmentAggregateContents>(Guid aggregateId)
        where TEnvironmentAggregate : AggregateBase<TEnvironmentAggregateContents>, new()
        where TEnvironmentAggregateContents : IAggregateContents, new()
    {
        return _helper.GetEnvironmentAggregateDto<TEnvironmentAggregate, TEnvironmentAggregateContents>(aggregateId);
    }
    public Guid RunEnvironmentCreateCommand<TEnvironmentAggregate>(
        ICreateAggregateCommand<TEnvironmentAggregate> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregate : AggregateCommonBase, new()
    {
        return _helper.RunEnvironmentCreateCommand(command, injectingAggregateId);
    }
    public void RunEnvironmentChangeCommand<TEnvironmentAggregate>(ChangeAggregateCommandBase<TEnvironmentAggregate> command)
        where TEnvironmentAggregate : AggregateCommonBase, new()
    {
        _helper.RunEnvironmentChangeCommand(command);
    }
    public IAggregateTestHelper<TAggregate, TContents> GivenEnvironmentCommandExecutorAction(Action<AggregateTestCommandExecutor> action)
    {
        _helper.GivenEnvironmentCommandExecutorAction(action);
        return this;
    }
    public IReadOnlyCollection<IAggregateEvent> GetLatestEnvironmentEvents()
    {
        return _helper.GetLatestEnvironmentEvents();
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

    public IAggregateTestHelper<TAggregate, TContents> ThenGetEvents(Action<List<IAggregateEvent>> checkEventsAction)
    {
        return _helper.ThenGetEvents(checkEventsAction);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenGetSingleEvent<T>(Action<T> checkEventAction) where T : IAggregateEvent
    {
        return _helper.ThenGetSingleEvent(checkEventAction);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEventIs<T>(AggregateEvent<T> aggregateEvent) where T : IEventPayload
    {
        return _helper.ThenSingleEventIs(aggregateEvent);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenSingleEventPayloadIs<T>(T payload) where T : IEventPayload
    {
        return _helper.ThenSingleEventPayloadIs(payload);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenGetSingleEventPayload<T>(Action<T> checkPayloadAction) where T : class, IEventPayload
    {
        return _helper.ThenGetSingleEventPayload(checkPayloadAction);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenGetState(Action<AggregateDto<TContents>> checkDtoAction)
    {
        return _helper.ThenGetState(checkDtoAction);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenStateIs(AggregateDto<TContents> expectedDto)
    {
        return _helper.ThenStateIs(expectedDto);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenGetContents(Action<TContents> contentsAction)
    {
        return _helper.ThenGetContents(contentsAction);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenContentsIs(TContents contents)
    {
        return _helper.ThenContentsIs(contents);
    }
    public IAggregateTestHelper<TAggregate, TContents> WriteStateToFile(string filename)
    {
        return _helper.WriteStateToFile(filename);
    }
    public IAggregateTestHelper<TAggregate, TContents> WriteContentsToFile(string filename)
    {
        return _helper.WriteStateToFile(filename);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenStateIsFromJson(string dtoJson)
    {
        return _helper.ThenStateIsFromJson(dtoJson);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenStateIsFromFile(string dtoFileName)
    {
        return _helper.ThenStateIsFromFile(dtoFileName);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenContentsIsFromJson(string contentsJson)
    {
        return _helper.ThenContentsIsFromJson(contentsJson);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenContentsIsFromFile(string contentsFileName)
    {
        return _helper.ThenContentsIsFromFile(contentsFileName);
    }
    public Guid GetAggregateId()
    {
        return _helper.GetAggregateId();
    }
    public int GetCurrentVersion()
    {
        return _helper.GetCurrentVersion();
    }
    public TAggregate GetAggregate()
    {
        return _helper.GetAggregate();
    }

    public IAggregateTestHelper<TAggregate, TContents> ThenThrows<T>() where T : Exception
    {
        return _helper.ThenThrows<T>();
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenGetException<T>(Action<T> checkException) where T : Exception
    {
        return _helper.ThenGetException(checkException);
    }
    public IAggregateTestHelper<TAggregate, TContents> ThenGetException(Action<Exception> checkException)
    {
        return _helper.ThenGetException(checkException);
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

    protected virtual void SetupDependency(IServiceCollection serviceCollection)
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
