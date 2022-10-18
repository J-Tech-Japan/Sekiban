using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Document;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Core.Shared;
using Sekiban.Testing.Command;
using Sekiban.Testing.QueryFilter;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
namespace Sekiban.Testing.Projection;

public abstract class CommonMultipleAggregateProjectionTestBase<TProjection, TProjectionContents, TDependencyDefinition> : IDisposable,
    IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents>, ITestHelperEventSubscriber
    where TProjection : IMultipleAggregateProjector<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
    where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly AggregateTestCommandExecutor _commandExecutor;
    protected readonly List<IQueryFilterChecker<MultipleAggregateProjectionContentsDto<TProjectionContents>>> _queryFilterCheckers = new();
    protected IServiceProvider _serviceProvider;
    public MultipleAggregateProjectionContentsDto<TProjectionContents> Dto { get; protected set; }
        = new(new TProjectionContents(), Guid.Empty, string.Empty, 0, 0);
    protected Exception? _latestException { get; set; }
    public Action<IAggregateEvent> OnEvent => e => GivenEvents(new List<IAggregateEvent> { e });

    public CommonMultipleAggregateProjectionTestBase()
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        services.AddQueryFiltersFromDependencyDefinition(new TDependencyDefinition());
        SekibanEventSourcingDependency.RegisterForAggregateTest(services, new TDependencyDefinition());
        _serviceProvider = services.BuildServiceProvider();
        _commandExecutor = new AggregateTestCommandExecutor(_serviceProvider);
    }
    public CommonMultipleAggregateProjectionTestBase(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _commandExecutor = new AggregateTestCommandExecutor(_serviceProvider);
    }

    public void Dispose()
    {
    }
    public Guid RunCreateCommand<TAggregate>(ICreateAggregateCommand<TAggregate> command, Guid? injectingAggregateId = null)
        where TAggregate : AggregateCommonBase, new()
    {
        var (events, aggregateId) = _commandExecutor.ExecuteCreateCommand(command, injectingAggregateId);
        return aggregateId;
    }
    public void RunChangeCommand<TAggregate>(ChangeAggregateCommandBase<TAggregate> command) where TAggregate : AggregateCommonBase, new()
    {
        var events = _commandExecutor.ExecuteChangeCommand(command);

    }

    public AggregateDto<TEnvironmentAggregateContents> GetAggregateDto<TEnvironmentAggregate, TEnvironmentAggregateContents>(Guid aggregateId)
        where TEnvironmentAggregate : AggregateBase<TEnvironmentAggregateContents>, new()
        where TEnvironmentAggregateContents : IAggregateContents, new()
    {
        var singleAggregateService = _serviceProvider.GetRequiredService(typeof(ISingleAggregateService)) as ISingleAggregateService;
        if (singleAggregateService is null) { throw new Exception("Failed to get single aggregate service"); }
        var aggregate = singleAggregateService.GetAggregateDtoAsync<TEnvironmentAggregate, TEnvironmentAggregateContents>(aggregateId).Result;
        return aggregate ?? throw new SekibanAggregateNotExistsException(aggregateId, typeof(TEnvironmentAggregate).Name);
    }
    public IReadOnlyCollection<IAggregateEvent> GetLatestEvents()
    {
        return _commandExecutor.LatestEvents;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(IEnumerable<IAggregateEvent> events)
    {
        var documentWriter = _serviceProvider.GetRequiredService(typeof(IDocumentWriter)) as IDocumentWriter;
        if (documentWriter is null) { throw new Exception("Failed to get document writer"); }
        var sekibanAggregateTypes = _serviceProvider.GetService<SekibanAggregateTypes>() ?? throw new Exception("Failed to get aggregate types");

        foreach (var e in events)
        {
            var aggregateType = sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == e.AggregateType);
            if (aggregateType is null) { throw new Exception($"Failed to find aggregate type {e.AggregateType}"); }
            documentWriter.SaveAsync(e, aggregateType.Aggregate).Wait();
        }
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(params IAggregateEvent[] events)
    {
        return GivenEvents(events.AsEnumerable());
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(string jsonEvents)
    {
        var list = JsonSerializer.Deserialize<List<JsonElement>>(jsonEvents);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }
    public async Task<IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents>> GivenEventsFromFileAsync(string filename)
    {
        await using var openStream = File.OpenRead(filename);
        var list = await JsonSerializer.DeserializeAsync<List<JsonElement>>(openStream);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }
    public abstract IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> WhenProjection();

    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenNotThrowsAnException()
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.Null(exception);
        return this;
    }

    public async Task<IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents>> ThenDtoFileAsync(string filename)
    {
        await using var openStream = File.OpenRead(filename);
        var projection = await JsonSerializer.DeserializeAsync<MultipleAggregateProjectionContentsDto<TProjectionContents>>(openStream);
        if (projection is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        return ThenDto(projection);
    }

    public async Task<IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents>> WriteProjectionToFileAsync(string filename)
    {
        var json = SekibanJsonHelper.Serialize(Dto);
        await File.WriteAllTextAsync(filename, json);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenDto(
        MultipleAggregateProjectionContentsDto<TProjectionContents> dto)
    {
        var actual = Dto;
        var expected = dto with { LastEventId = actual.LastEventId, LastSortableUniqueId = actual.LastSortableUniqueId, Version = actual.Version };
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenContents(TProjectionContents contents)
    {
        var actual = Dto.Contents;
        var expected = contents;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenDtoFile(string filename)
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<MultipleAggregateProjectionContentsDto<TProjectionContents>>(openStream);
        if (projection is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        return ThenDto(projection);
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> WriteProjectionToFile(string filename)
    {
        var json = SekibanJsonHelper.Serialize(Dto);
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEventsFromFile(string filename)
    {
        using var openStream = File.OpenRead(filename);
        var list = JsonSerializer.Deserialize<List<JsonElement>>(openStream);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }

    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenQueryFilterChecker(
        IQueryFilterChecker<MultipleAggregateProjectionContentsDto<TProjectionContents>> checker)
    {
        if (_serviceProvider is null) { throw new Exception("Service provider is null. Please setup service provider."); }
        checker.QueryFilterHandler = _serviceProvider.GetService<QueryFilterHandler>();
        _queryFilterCheckers.Add(checker);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(
        params (Guid aggregateId, Type aggregateType, IEventPayload payload)[] eventTouples)
    {
        foreach (var (aggregateId, aggregateType, payload) in eventTouples)
        {
            var type = payload.GetType();
            var isCreateEvent = payload is ICreatedEventPayload;
            var eventType = typeof(AggregateEvent<>);
            var genericType = eventType.MakeGenericType(type);
            var ev = Activator.CreateInstance(genericType, aggregateId, aggregateType, payload, isCreateEvent) as IAggregateEvent;
            if (ev == null) { throw new InvalidDataException("イベントの生成に失敗しました。" + payload); }
            GivenEvents(ev);
        }
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(
        params (Guid aggregateId, IEventPayload payload)[] eventTouples)
    {
        foreach (var (aggregateId, payload) in eventTouples)
        {
            var type = payload.GetType();
            var isCreateEvent = payload is ICreatedEventPayload;
            var interfaces = payload.GetType().GetInterfaces();
            var interfaceType = payload.GetType()
                .GetInterfaces()
                ?.FirstOrDefault(m => m.IsGenericType && m.GetGenericTypeDefinition() == typeof(IAggregatePointerEvent<>));
            var aggregateType = interfaceType?.GenericTypeArguments?.FirstOrDefault();
            if (aggregateType is null) { throw new InvalidDataException("イベントの生成に失敗しました。" + payload); }
            var eventType = typeof(AggregateEvent<>);
            var genericType = eventType.MakeGenericType(type);
            var ev = Activator.CreateInstance(genericType, aggregateId, aggregateType, payload, isCreateEvent) as IAggregateEvent;
            if (ev == null) { throw new InvalidDataException("イベントの生成に失敗しました。" + payload); }
            GivenEvents(ev);
        }
        return this;
    }

    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenCommandExecutorAction(
        Action<AggregateTestCommandExecutor> action)
    {
        action(_commandExecutor);
        return this;
    }

    public T GetService<T>() where T : notnull
    {
        if (_serviceProvider is null) { throw new Exception("Service provider is null. Please setup service provider."); }
        return _serviceProvider.GetRequiredService<T>() ?? throw new Exception($"Service {typeof(T)} not found");
    }

    private void AddEventsFromList(List<JsonElement> list)
    {
        if (_serviceProvider is null) { throw new Exception("Service provider is null. Please setup service provider."); }
        var registeredEventTypes = _serviceProvider.GetService<RegisteredEventTypes>();
        if (registeredEventTypes is null) { throw new InvalidOperationException("RegisteredEventTypes が登録されていません。"); }
        foreach (var json in list)
        {
            var documentTypeName = json.GetProperty("DocumentTypeName").ToString();
            var eventPayloadType = registeredEventTypes.RegisteredTypes.FirstOrDefault(e => e.Name == documentTypeName);
            if (eventPayloadType is null)
            {
                throw new InvalidDataException($"イベントタイプ {documentTypeName} は登録されていません。");
            }
            var eventType = typeof(AggregateEvent<>).MakeGenericType(eventPayloadType);
            if (eventType is null)
            {
                throw new InvalidDataException($"イベント {documentTypeName} の生成に失敗しました。");
            }
            var eventInstance = JsonSerializer.Deserialize(json.ToString(), eventType);
            if (eventInstance is null)
            {
                throw new InvalidDataException($"イベント {documentTypeName} のデシリアライズに失敗しました。");
            }
            var ev = eventInstance as IAggregateEvent;
            if (ev is null) { throw new InvalidDataException($"イベント {documentTypeName} の生成に失敗しました。"); }
            GivenEvents(ev);
        }
    }
    protected virtual void SetupDependency(IServiceCollection serviceCollection)
    {

    }

    private void ResetBeforeCommand()
    {
        _latestException = null;
    }
}
