using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Orleans.Parts;
using System.Text.Json;
namespace Sekiban.Pure.Orleans.Grains;

public class AggregateProjectorGrain(
    [PersistentState("aggregate", "Default")] IPersistentState<SerializableAggregate> state,
    SekibanDomainTypes sekibanDomainTypes,
    IServiceProvider serviceProvider) : Grain, IAggregateProjectorGrain
{
    private Aggregate _currentAggregate = Aggregate.Empty;
    private OptionalValue<PartitionKeysAndProjector> _partitionKeysAndProjector
        = OptionalValue<PartitionKeysAndProjector>.Empty;
    private IGrainTimer? _timer;
    private bool UpdatedAfterWrite { get; set; }

    private JsonSerializerOptions JsonOptions => sekibanDomainTypes.JsonSerializerOptions;

    public async Task<Aggregate> GetStateAsync()
    {
        await state.ReadStateAsync();
        var eventGrain = GrainFactory.GetGrain<IAggregateEventHandlerGrain>(
            GetPartitionKeysAndProjector().ToEventHandlerGrainKey());
        return await GetStateInternalAsync(eventGrain);
    }

    public async Task<CommandResponse> ExecuteCommandAsync(
        ICommandWithHandlerSerializable orleansCommand,
        CommandMetadata metadata)
    {
        var eventGrain = GrainFactory.GetGrain<IAggregateEventHandlerGrain>(
            GetPartitionKeysAndProjector().ToEventHandlerGrainKey());

        if (_currentAggregate == null || _currentAggregate == Aggregate.Empty)
        {
            _currentAggregate = await GetStateInternalAsync(eventGrain);
        }

        // 楽観的並行性制御のためのリトライ機能を追加
        const int maxRetries = 3;
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var orleansRepository = new OrleansRepository(
                    eventGrain,
                    GetPartitionKeysAndProjector().PartitionKeys,
                    GetPartitionKeysAndProjector().Projector,
                    sekibanDomainTypes.EventTypes,
                    _currentAggregate);

                var commandExecutor = new CommandExecutor(serviceProvider)
                    { EventTypes = sekibanDomainTypes.EventTypes };

                var result = await sekibanDomainTypes
                    .CommandTypes
                    .ExecuteGeneral(
                        commandExecutor,
                        orleansCommand,
                        GetPartitionKeysAndProjector().PartitionKeys,
                        metadata,
                        (_, _) => orleansRepository.GetAggregate(),
                        orleansRepository.Save)
                    .UnwrapBox();

                _currentAggregate = orleansRepository.GetProjectedAggregate(result.Events).UnwrapBox();
                UpdatedAfterWrite = true;

                return new CommandResponse(
                    GetPartitionKeysAndProjector().PartitionKeys,
                    result.Events,
                    _currentAggregate.Version);
            }
            catch (InvalidCastException ex) when (ex.Message.Contains("Expected last event ID does not match") &&
                attempt < maxRetries - 1)
            {
                await Task.Delay(50 * (attempt + 1));
                _currentAggregate = await GetStateInternalAsync(eventGrain);
            }
        }

        // 最大リトライ回数に達した場合は例外を再スロー
        throw new InvalidOperationException(
            "Failed to execute command after maximum retries due to concurrency conflicts");
    }

    public async Task<Aggregate> RebuildStateAsync() => await RebuildStateInternalAsync();

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        await state.ReadStateAsync();

        var eventGrain = GrainFactory.GetGrain<IAggregateEventHandlerGrain>(
            GetPartitionKeysAndProjector().ToEventHandlerGrainKey());

        _currentAggregate = await GetStateInternalAsync(eventGrain);

        _timer = this.RegisterGrainTimer(
            Callback,
            state,
            new GrainTimerCreationOptions
                { DueTime = TimeSpan.FromSeconds(10), Period = TimeSpan.FromSeconds(10), Interleave = true });
    }

    public async Task Callback(object currentState)
    {
        if (UpdatedAfterWrite)
        {
            await WriteSerializableStateAsync();
            UpdatedAfterWrite = false;
        }
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await base.OnDeactivateAsync(reason, cancellationToken);
        if (UpdatedAfterWrite)
        {
            await WriteSerializableStateAsync();
            UpdatedAfterWrite = false;
        }

        if (_timer != null)
        {
            _timer.Dispose();
        }
    }

    private PartitionKeysAndProjector GetPartitionKeysAndProjector()
    {
        if (_partitionKeysAndProjector.HasValue) return _partitionKeysAndProjector.GetValue();
        _partitionKeysAndProjector = PartitionKeysAndProjector
            .FromGrainKey(this.GetPrimaryKeyString(), sekibanDomainTypes.AggregateProjectorSpecifier)
            .UnwrapBox();
        return _partitionKeysAndProjector.GetValue();
    }

    private async Task<Aggregate> GetStateInternalAsync(IAggregateEventHandlerGrain eventHandlerGrain)
    {
        var projector = GetPartitionKeysAndProjector().Projector;
        var serializableState = state.State;

        if (serializableState != null)
        {
            var aggregateOptional = await serializableState.ToAggregateAsync(sekibanDomainTypes);
            if (aggregateOptional.HasValue)
            {
                var aggregate = aggregateOptional.GetValue();

                if (projector.GetVersion() != aggregate.ProjectorVersion)
                {
                    UpdatedAfterWrite = true;
                    return await RebuildStateInternalAsync();
                }

                if (aggregate.Version == 0)
                {
                    return Aggregate.EmptyFromPartitionKeys(GetPartitionKeysAndProjector().PartitionKeys);
                }

                if (await eventHandlerGrain.GetLastSortableUniqueIdAsync() != aggregate.LastSortableUniqueId)
                {
                    var events = await eventHandlerGrain.GetDeltaEventsAsync(aggregate.LastSortableUniqueId);
                    aggregate = aggregate.Project(events.ToList(), projector).UnwrapBox();
                    _currentAggregate = aggregate;
                    UpdatedAfterWrite = true;
                }

                return aggregate;
            }
        }

        // state が無い / 復元失敗の場合はイベントGrainから全イベントを取得して再構築
        UpdatedAfterWrite = true;
        return await RebuildStateInternalAsync();
    }

    private async Task WriteSerializableStateAsync()
    {
        state.State = await SerializableAggregate.CreateFromAsync(_currentAggregate, JsonOptions);
        await state.WriteStateAsync();
    }

    private async Task<Aggregate> RebuildStateInternalAsync()
    {
        var eventGrain = GrainFactory.GetGrain<IAggregateEventHandlerGrain>(
            GetPartitionKeysAndProjector().ToEventHandlerGrainKey());

        var orleansRepository = new OrleansRepository(
            eventGrain,
            GetPartitionKeysAndProjector().PartitionKeys,
            GetPartitionKeysAndProjector().Projector,
            sekibanDomainTypes.EventTypes,
            Aggregate.EmptyFromPartitionKeys(GetPartitionKeysAndProjector().PartitionKeys));

        var aggregate = await orleansRepository.Load().UnwrapBox();
        _currentAggregate = aggregate;

        await WriteSerializableStateAsync();

        return aggregate;
    }
}
