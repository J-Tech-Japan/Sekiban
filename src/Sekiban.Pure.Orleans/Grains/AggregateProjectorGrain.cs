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
    private OptionalValue<PartitionKeysAndProjector> _partitionKeysAndProjector
        = OptionalValue<PartitionKeysAndProjector>.Empty;
    private Aggregate _currentAggregate = Aggregate.Empty;
    private bool UpdatedAfterWrite { get; set; }
    private IGrainTimer? _timer;
    
    // JSONシリアライザオプション
    private JsonSerializerOptions JsonOptions => sekibanDomainTypes.JsonSerializerOptions;
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        
        // アクティベーション時に状態を読み込み
        await state.ReadStateAsync();
        
        // イベントGrainを取得
        var eventGrain = GrainFactory.GetGrain<IAggregateEventHandlerGrain>(
            GetPartitionKeysAndProjector().ToEventHandlerGrainKey());
            
        // 内部状態の初期化
        _currentAggregate = await GetStateInternalAsync(eventGrain);
        
        // 定期的な状態保存タイマーを設定
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
    
    public async Task<Aggregate> GetStateAsync()
    {
        await state.ReadStateAsync();
        var eventGrain = GrainFactory.GetGrain<IAggregateEventHandlerGrain>(
            GetPartitionKeysAndProjector().ToEventHandlerGrainKey());
        return await GetStateInternalAsync(eventGrain);
    }
    
    private async Task<Aggregate> GetStateInternalAsync(IAggregateEventHandlerGrain eventHandlerGrain)
    {
        // SerializableAggregateからAggregateに変換を試みる
        var projector = GetPartitionKeysAndProjector().Projector;
        var serializableState = state.State;
        
        if (serializableState != null)
        {
            var aggregateOptional = await serializableState.ToAggregateAsync(sekibanDomainTypes);
            if (aggregateOptional.HasValue)
            {
                var aggregate = aggregateOptional.GetValue();
                
                // プロジェクターバージョンが一致しない場合は再構築
                if (projector.GetVersion() != aggregate.ProjectorVersion)
                {
                    UpdatedAfterWrite = true;
                    return await RebuildStateInternalAsync();
                }
                
                // バージョンが0の場合は空の集約を返す
                if (aggregate.Version == 0)
                {
                    return Aggregate.EmptyFromPartitionKeys(GetPartitionKeysAndProjector().PartitionKeys);
                }
                
                // 最新イベントまで更新
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
        
        // 変換に失敗した場合や状態がnullの場合は再構築
        UpdatedAfterWrite = true;
        return await RebuildStateInternalAsync();
    }

    // シリアライズ可能な状態を保存
    private async Task WriteSerializableStateAsync()
    {
        state.State = await SerializableAggregate.CreateFromAsync(_currentAggregate, JsonOptions);
        await state.WriteStateAsync();
    }

    public async Task<CommandResponse> ExecuteCommandAsync(
        ICommandWithHandlerSerializable orleansCommand,
        CommandMetadata metadata)
    {
        var eventGrain = GrainFactory.GetGrain<IAggregateEventHandlerGrain>(
            GetPartitionKeysAndProjector().ToEventHandlerGrainKey());
            
        // _currentAggregateが空またはnullの場合、再度取得
        if (_currentAggregate == null || _currentAggregate == Aggregate.Empty)
        {
            _currentAggregate = await GetStateInternalAsync(eventGrain);
        }
            
        var orleansRepository = new OrleansRepository(
            eventGrain,
            GetPartitionKeysAndProjector().PartitionKeys,
            GetPartitionKeysAndProjector().Projector,
            sekibanDomainTypes.EventTypes,
            _currentAggregate);
            
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
            
        _currentAggregate = orleansRepository.GetProjectedAggregate(result.Events).UnwrapBox();
        UpdatedAfterWrite = true;
        
        return result;
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

    public async Task<Aggregate> RebuildStateAsync()
    {
        return await RebuildStateInternalAsync();
    }
}
