using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Orleans.Parts;
namespace Sekiban.Pure.Orleans.Grains;

public class AggregateProjectorGrain(
    [PersistentState("aggregate", "Default")]
    IPersistentState<Aggregate> state,
    SekibanDomainTypes sekibanDomainTypes,
    IServiceProvider serviceProvider) : Grain, IAggregateProjectorGrain
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
    public async Task<Aggregate> GetStateAsync()
    {
        await state.ReadStateAsync();
        var eventGrain
            = GrainFactory.GetGrain<IAggregateEventHandlerGrain>(
                GetPartitionKeysAndProjector().ToEventHandlerGrainKey());
        var read = await GetStateInternalAsync(eventGrain);
        return read;
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
                    events.ToList(),
                    GetPartitionKeysAndProjector().Projector)
                .UnwrapBox();
            UpdatedAfterWrite = true;
        }
        return read;
    }

    public async Task<CommandResponse> ExecuteCommandAsync(
        ICommandWithHandlerSerializable orleansCommand,
        CommandMetadata metadata)
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
        var commandExecutor = new CommandExecutor(serviceProvider) { EventTypes = sekibanDomainTypes.EventTypes };
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
        state.State = orleansRepository.GetProjectedAggregate(result.Events).UnwrapBox();
        UpdatedAfterWrite = true;
        return result;
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

    public async Task<Aggregate> RebuildStateAsync()
    {
        var aggregate = await RebuildStateInternalAsync();
        return aggregate;
    }
}