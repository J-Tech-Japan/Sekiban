using Sekiban.Core.Aggregate;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Types;
namespace Sekiban.Core.Snapshot.BackgroundServices;

public abstract class MultiProjectionSnapshotGenerateSettingAbstract : IMultiProjectionsSnapshotGenerateSetting
{
    protected List<Type> MultiProjectionsSnapshotTypes { get; } = new();
    protected List<Type> AggregateListSnapshotTypes { get; } = new();
    protected List<Type> SingleProjectionListSnapshotTypes { get; } = new();
    protected int MinimumNumberOfEventsToGenerateSnapshot { get; set; } = 3000;
    protected List<string> RootPartitionKeys { get; } = new();
    public int ExecuteIntervalSeconds { get; set; } = 3600;
    protected MultiProjectionSnapshotGenerateSettingAbstract()
    {
        // ReSharper disable once VirtualMemberCallInConstructor
        Define();
    }
    public IEnumerable<Type> GetMultiProjectionsSnapshotTypes() => MultiProjectionsSnapshotTypes;
    public IEnumerable<Type> GetAggregateListSnapshotTypes() => AggregateListSnapshotTypes;
    public IEnumerable<Type> GetSingleProjectionListSnapshotTypes() => SingleProjectionListSnapshotTypes;

    public int GetExecuteIntervalSeconds() => ExecuteIntervalSeconds;
    public int GetMinimumNumberOfEventsToGenerateSnapshot() => MinimumNumberOfEventsToGenerateSnapshot;
    public IEnumerable<string> GetRootPartitionKeys() =>
        RootPartitionKeys.Count == 0 ? new[] { IMultiProjectionService.ProjectionAllRootPartitions } : RootPartitionKeys;

    public MultiProjectionSnapshotGenerateSettingAbstract AddMultiProjectionsSnapshotType<T>() where T : IMultiProjectionPayloadCommon
    {
        MultiProjectionsSnapshotTypes.Add(typeof(T));
        return this;
    }
    public MultiProjectionSnapshotGenerateSettingAbstract AddAggregateListSnapshotType<T>() where T : IAggregatePayloadCommon
    {
        AggregateListSnapshotTypes.Add(typeof(T));
        return this;
    }
    public MultiProjectionSnapshotGenerateSettingAbstract AddSingleProjectionListSnapshotType<T>() where T : ISingleProjectionPayloadCommon
    {
        SingleProjectionListSnapshotTypes.Add(typeof(T));
        return this;
    }

    public MultiProjectionSnapshotGenerateSettingAbstract AddRootPartitionKey(string rootPartitionKey)
    {
        RootPartitionKeys.Add(rootPartitionKey);
        return this;
    }
    public MultiProjectionSnapshotGenerateSettingAbstract AddProjectionAllRootPartitionKey()
    {
        RootPartitionKeys.Add(IMultiProjectionService.ProjectionAllRootPartitions);
        return this;
    }
    public MultiProjectionSnapshotGenerateSettingAbstract SetExecuteIntervalSeconds(int executeIntervalSeconds)
    {
        ExecuteIntervalSeconds = executeIntervalSeconds;
        return this;
    }
    public MultiProjectionSnapshotGenerateSettingAbstract SetMinimumNumberOfEventsToGenerateSnapshot(int minimumNumberOfEventsToGenerateSnapshot)
    {
        MinimumNumberOfEventsToGenerateSnapshot = minimumNumberOfEventsToGenerateSnapshot;
        return this;
    }
    public MultiProjectionSnapshotGenerateSettingAbstract AddAllAggregatesFromDependency<TDependency>()
        where TDependency : IDependencyDefinition, new()
    {
        var dependencyDefinition = new TDependency();
        foreach (var aggregateDefinition in dependencyDefinition.GetAggregateDefinitions())
        {
            AggregateListSnapshotTypes.Add(aggregateDefinition.AggregateType);
        }
        return this;
    }
    public MultiProjectionSnapshotGenerateSettingAbstract AddAllSingleProjectionFromDependency<TDependency>()
        where TDependency : IDependencyDefinition, new()
    {
        var dependencyDefinition = new TDependency();
        foreach (var aggregateDefinition in dependencyDefinition.GetAggregateDefinitions())
        {
            foreach (var singleProjection in aggregateDefinition.SingleProjectionTypes)
            {
                SingleProjectionListSnapshotTypes.Add(singleProjection);
            }
        }
        return this;
    }
    public MultiProjectionSnapshotGenerateSettingAbstract AddAllMultiProjectionFromDependency<TDependency>()
        where TDependency : IDependencyDefinition, new()
    {
        var dependencyDefinition = new TDependency();
        foreach (var multiProjection in dependencyDefinition.GetExecutingAssembly().DefinedTypes.Where(m => m.IsMultiProjectionPayloadType()))
        {
            MultiProjectionsSnapshotTypes.Add(multiProjection);
        }
        return this;
    }
    public MultiProjectionSnapshotGenerateSettingAbstract AddAllFromDependency<TDependency>() where TDependency : IDependencyDefinition, new()
    {
        AddAllAggregatesFromDependency<TDependency>();
        AddAllSingleProjectionFromDependency<TDependency>();
        AddAllMultiProjectionFromDependency<TDependency>();
        return this;
    }

    public abstract void Define();
}
