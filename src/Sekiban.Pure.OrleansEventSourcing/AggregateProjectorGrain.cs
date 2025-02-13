using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
namespace Sekiban.Pure.OrleansEventSourcing;

public class AggregateProjectorGrain(
    [PersistentState("aggregate", "Default")]
    IPersistentState<Aggregate> state,
    SekibanDomainTypes sekibanDomainTypes) : Grain, IAggregateProjectorGrain
{
    private OptionalValue<PartitionKeysAndProjector> _partitionKeysAndProjector
        = OptionalValue<PartitionKeysAndProjector>.Empty;
    private bool UpdatedAfterWrite { get; set; }
    private IGrainTimer? _timer;
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        // アクティベーション時に読み込み
        await state.ReadStateAsync();
        var eventGrain
            = GrainFactory.GetGrain<IAggregateEventHandlerGrain>(
                GetPartitionKeysAndProjector().ToEventHandlerGrainKey());
        state.State = await GetStateInternalAsync(eventGrain);
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
            await state.WriteStateAsync();
            UpdatedAfterWrite = false;
        }
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await base.OnDeactivateAsync(reason, cancellationToken);
        if (UpdatedAfterWrite)
        {
            await state.WriteStateAsync();
            UpdatedAfterWrite = false;
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
    public async Task<OrleansAggregate> GetStateAsync()
    {
        await state.ReadStateAsync();
        var eventGrain
            = GrainFactory.GetGrain<IAggregateEventHandlerGrain>(
                GetPartitionKeysAndProjector().ToEventHandlerGrainKey());
        var read = await GetStateInternalAsync(eventGrain);
        return read.ToOrleansAggregate();
    }
    private async Task<Aggregate> GetStateInternalAsync(IAggregateEventHandlerGrain eventHandlerGrain)
    {
        var read = state.State;
        if (read == null || GetPartitionKeysAndProjector().Projector.GetVersion() != read.ProjectorVersion)
        {
            UpdatedAfterWrite = true;
            return await RebuildStateInternalAsync();
        }
        if (read.Version == 0)
        {
            return Aggregate.EmptyFromPartitionKeys(GetPartitionKeysAndProjector().PartitionKeys);
        }
        if (await eventHandlerGrain.GetLastSortableUniqueIdAsync() != read.LastSortableUniqueId)
        {
            var events = await eventHandlerGrain.GetDeltaEventsAsync(read.LastSortableUniqueId);
            read = read
                .Project(
                    events.ToList().ToEvents(sekibanDomainTypes.EventTypes),
                    GetPartitionKeysAndProjector().Projector)
                .UnwrapBox();
            UpdatedAfterWrite = true;
        }
        return read;
    }

    public async Task<OrleansCommandResponse> ExecuteCommandAsync(
        ICommandWithHandlerSerializable orleansCommand,
        OrleansCommandMetadata metadata)
    {
        var eventGrain
            = GrainFactory.GetGrain<IAggregateEventHandlerGrain>(
                GetPartitionKeysAndProjector().ToEventHandlerGrainKey());
        var orleansRepository = new OrleansRepository(
            eventGrain,
            GetPartitionKeysAndProjector().PartitionKeys,
            GetPartitionKeysAndProjector().Projector,
            sekibanDomainTypes.EventTypes,
            await GetStateInternalAsync(eventGrain));
        var commandExecutor = new CommandExecutor { EventTypes = sekibanDomainTypes.EventTypes };
        var result = await commandExecutor
            .ExecuteGeneralNonGeneric(
                orleansCommand,
                GetPartitionKeysAndProjector().Projector,
                GetPartitionKeysAndProjector().PartitionKeys,
                NoInjection.Empty,
                orleansCommand.GetHandler(),
                orleansCommand.GetAggregatePayloadType(),
                metadata.ToCommandMetadata(),
                (_, _) => orleansRepository.GetAggregate(),
                orleansRepository.Save)
            .UnwrapBox();
        state.State = orleansRepository.GetProjectedAggregate(result.Events).UnwrapBox();
        UpdatedAfterWrite = true;
        return result.ToOrleansCommandResponse();
    }

    private async Task<Aggregate> RebuildStateInternalAsync()
    {
        var eventGrain
            = GrainFactory.GetGrain<IAggregateEventHandlerGrain>(
                GetPartitionKeysAndProjector().ToEventHandlerGrainKey());
        var orleansRepository = new OrleansRepository(
            eventGrain,
            GetPartitionKeysAndProjector().PartitionKeys,
            GetPartitionKeysAndProjector().Projector,
            sekibanDomainTypes.EventTypes,
            Aggregate.EmptyFromPartitionKeys(GetPartitionKeysAndProjector().PartitionKeys));
        var aggregate = await orleansRepository.Load().UnwrapBox();
        state.State = aggregate;
        await state.WriteStateAsync();
        return aggregate;
    }

    public async Task<OrleansAggregate> RebuildStateAsync()
    {
        var aggregate = await RebuildStateInternalAsync();
        return aggregate.ToOrleansAggregate();
    }
}