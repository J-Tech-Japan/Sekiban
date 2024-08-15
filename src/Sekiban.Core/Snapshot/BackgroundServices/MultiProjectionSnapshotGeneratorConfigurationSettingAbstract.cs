using Microsoft.Extensions.Configuration;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Types;
namespace Sekiban.Core.Snapshot.BackgroundServices;

/// <summary>
///     Multi Projection snapshot generate setting abstract class.
///     Developer who wants to generate snapshot can inherit this class and implement Define method.
///     This class allows user to use configuration to get value from settings.
/// </summary>
public abstract class
    MultiProjectionSnapshotGeneratorConfigurationSettingAbstract : IMultiProjectionsSnapshotGenerateSetting
{
    protected readonly IConfiguration? _configuration;

    protected List<Type> MultiProjectionsSnapshotTypes { get; } = [];
    protected List<Type> AggregateListSnapshotTypes { get; } = [];
    protected List<Type> SingleProjectionListSnapshotTypes { get; } = [];
    protected int MinimumNumberOfEventsToGenerateSnapshot { get; set; } = 3000;
    protected List<string> RootPartitionKeys { get; } = [];
    public int ExecuteIntervalSeconds { get; set; } = 3600;

    protected MultiProjectionSnapshotGeneratorConfigurationSettingAbstract(IConfiguration configuration)
    {
        _configuration = configuration;
        // ReSharper disable once VirtualMemberCallInConstructor
        Define();
    }

    public IEnumerable<Type> GetMultiProjectionsSnapshotTypes() => MultiProjectionsSnapshotTypes;
    public IEnumerable<Type> GetAggregateListSnapshotTypes() => AggregateListSnapshotTypes;
    public IEnumerable<Type> GetSingleProjectionListSnapshotTypes() => SingleProjectionListSnapshotTypes;

    public int GetExecuteIntervalSeconds() => ExecuteIntervalSeconds;
    public int GetMinimumNumberOfEventsToGenerateSnapshot() => MinimumNumberOfEventsToGenerateSnapshot;
    public IEnumerable<string> GetRootPartitionKeys() =>
        RootPartitionKeys.Count == 0
            ? new[] { IMultiProjectionService.ProjectionAllRootPartitions }
            : RootPartitionKeys;

    public MultiProjectionSnapshotGeneratorConfigurationSettingAbstract AddMultiProjectionsSnapshotType<T>()
        where T : IMultiProjectionPayloadCommon
    {
        MultiProjectionsSnapshotTypes.Add(typeof(T));
        return this;
    }
    public MultiProjectionSnapshotGeneratorConfigurationSettingAbstract AddAggregateListSnapshotType<T>()
        where T : IAggregatePayloadCommon
    {
        AggregateListSnapshotTypes.Add(typeof(T));
        return this;
    }
    public MultiProjectionSnapshotGeneratorConfigurationSettingAbstract AddSingleProjectionListSnapshotType<T>()
        where T : ISingleProjectionPayloadCommon
    {
        SingleProjectionListSnapshotTypes.Add(typeof(T));
        return this;
    }

    public MultiProjectionSnapshotGeneratorConfigurationSettingAbstract AddRootPartitionKey(string rootPartitionKey)
    {
        RootPartitionKeys.Add(rootPartitionKey);
        return this;
    }
    public MultiProjectionSnapshotGeneratorConfigurationSettingAbstract AddProjectionAllRootPartitionKey()
    {
        RootPartitionKeys.Add(IMultiProjectionService.ProjectionAllRootPartitions);
        return this;
    }
    public MultiProjectionSnapshotGeneratorConfigurationSettingAbstract SetExecuteIntervalSeconds(
        int executeIntervalSeconds)
    {
        ExecuteIntervalSeconds = executeIntervalSeconds;
        return this;
    }
    public MultiProjectionSnapshotGeneratorConfigurationSettingAbstract SetMinimumNumberOfEventsToGenerateSnapshot(
        int minimumNumberOfEventsToGenerateSnapshot)
    {
        MinimumNumberOfEventsToGenerateSnapshot = minimumNumberOfEventsToGenerateSnapshot;
        return this;
    }
    public MultiProjectionSnapshotGeneratorConfigurationSettingAbstract AddAllAggregatesFromDependency<TDependency>()
        where TDependency : IDependencyDefinition, new()
    {
        var dependencyDefinition = new TDependency();
        foreach (var aggregateDefinition in dependencyDefinition.GetAggregateDefinitions())
        {
            AggregateListSnapshotTypes.Add(aggregateDefinition.AggregateType);
        }
        return this;
    }
    public MultiProjectionSnapshotGeneratorConfigurationSettingAbstract
        AddAllSingleProjectionFromDependency<TDependency>() where TDependency : IDependencyDefinition, new()
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
    public MultiProjectionSnapshotGeneratorConfigurationSettingAbstract
        AddAllMultiProjectionFromDependency<TDependency>() where TDependency : IDependencyDefinition, new()
    {
        var dependencyDefinition = new TDependency();
        foreach (var multiProjection in dependencyDefinition
            .GetExecutingAssembly()
            .DefinedTypes
            .Where(m => m.IsMultiProjectionPayloadType()))
        {
            MultiProjectionsSnapshotTypes.Add(multiProjection);
        }
        return this;
    }
    public MultiProjectionSnapshotGeneratorConfigurationSettingAbstract AddAllFromDependency<TDependency>()
        where TDependency : IDependencyDefinition, new()
    {
        AddAllAggregatesFromDependency<TDependency>();
        AddAllSingleProjectionFromDependency<TDependency>();
        AddAllMultiProjectionFromDependency<TDependency>();
        return this;
    }

    public abstract void Define();
}
