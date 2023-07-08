using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Types;
using System.Reflection;
namespace Sekiban.Core.Snapshot.BackgroundServices;

public class MultiProjectionCollectionGenerator
{
    private readonly IMultiProjectionSnapshotGenerator _multiProjectionSnapshotGenerator;
    public MultiProjectionCollectionGenerator(IMultiProjectionSnapshotGenerator multiProjectionSnapshotGenerator) =>
        _multiProjectionSnapshotGenerator = multiProjectionSnapshotGenerator;

    public async Task GenerateAsync(IMultiProjectionsSnapshotGenerateSetting settings)
    {
        var minimumNumberOfEventsToGenerateSnapshot = settings.GetMinimumNumberOfEventsToGenerateSnapshot();
        foreach (var multiProjectionType in settings.GetMultiProjectionsSnapshotTypes())
        {
            if (multiProjectionType.IsMultiProjectionPayloadType())
            {
                var method = typeof(IMultiProjectionSnapshotGenerator).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(
                        info => info.Name == nameof(IMultiProjectionSnapshotGenerator.GenerateMultiProjectionSnapshotAsync) &&
                            info.GetGenericArguments().Length == 1);

                if (method is null) { continue; }

                var generateMethod = method.MakeGenericMethod(multiProjectionType);

                var rootPartitionKeys = settings.GetRootPartitionKeys();
                foreach (var rootPartitionKey in rootPartitionKeys)
                {
                    await (dynamic)generateMethod.Invoke(
                        _multiProjectionSnapshotGenerator,
                        new object[] { minimumNumberOfEventsToGenerateSnapshot, rootPartitionKey })!;
                }
            }
        }
        foreach (var aggregatePayloadType in settings.GetAggregateListSnapshotTypes())
        {
            if (aggregatePayloadType.IsAggregatePayloadType())
            {
                var method = typeof(IMultiProjectionSnapshotGenerator).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(
                        info => info.Name == nameof(IMultiProjectionSnapshotGenerator.GenerateAggregateListSnapshotAsync) &&
                            info.GetGenericArguments().Length == 1);

                if (method is null) { continue; }

                var generateMethod = method.MakeGenericMethod(aggregatePayloadType);

                var rootPartitionKeys = settings.GetRootPartitionKeys();
                foreach (var rootPartitionKey in rootPartitionKeys)
                {
                    await (dynamic)generateMethod.Invoke(
                        _multiProjectionSnapshotGenerator,
                        new object[] { minimumNumberOfEventsToGenerateSnapshot, rootPartitionKey })!;
                }
            }
        }
        foreach (var singleProjectionPayloadType in settings.GetSingleProjectionListSnapshotTypes())
        {
            if (singleProjectionPayloadType.IsSingleProjectionPayloadType())
            {
                var method = typeof(IMultiProjectionSnapshotGenerator).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(
                        info => info.Name == nameof(IMultiProjectionSnapshotGenerator.GenerateSingleProjectionListSnapshotAsync) &&
                            info.GetGenericArguments().Length == 1);

                if (method is null) { continue; }

                var generateMethod = method.MakeGenericMethod(singleProjectionPayloadType);

                var rootPartitionKeys = settings.GetRootPartitionKeys();
                foreach (var rootPartitionKey in rootPartitionKeys)
                {
                    await (dynamic)generateMethod.Invoke(
                        _multiProjectionSnapshotGenerator,
                        new object[] { minimumNumberOfEventsToGenerateSnapshot, rootPartitionKey })!;
                }
            }
        }
    }
}
