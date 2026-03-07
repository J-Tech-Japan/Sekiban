using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Grains;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class MultiProjectionGrainFilteringAndCompressionTests
{
    [Fact]
    public void FilterByPositionAndProcessed_ShouldKeepEarlierPassingEvents_WhenLaterEventIsFiltered()
    {
        var grain = MultiProjectionGrainRetentionCompactionTestsAccessor.CreateGrain();
        var processedEventIds = MultiProjectionGrainRetentionCompactionTestsAccessor.GetField<HashSet<Guid>>(grain, "_processedEventIds");

        var first = new FilterCandidate(Guid.NewGuid(), "0001");
        var filteredOut = new FilterCandidate(Guid.NewGuid(), "0002");
        var last = new FilterCandidate(Guid.NewGuid(), "0003");
        processedEventIds.Add(filteredOut.Id);

        SetCatchUpCurrentPosition(grain, null);

        var filtered = InvokeFilter(
            grain,
            new[] { first, filteredOut, last },
            candidate => candidate.Id,
            candidate => candidate.SortableUniqueIdValue);

        Assert.Collection(
            filtered,
            candidate => Assert.Equal(first.Id, candidate.Id),
            candidate => Assert.Equal(last.Id, candidate.Id));
    }

    [Fact]
    public void CompressJsonAotOverload_ShouldReturnCompleteGzipPayload()
    {
        var payload = new CompressionPayload("alpha", 42);
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        var typeInfo = (JsonTypeInfo<CompressionPayload>)options.GetTypeInfo(typeof(CompressionPayload));

        var (compressedBytes, originalSizeBytes) = GzipCompression.CompressJson(
            payload,
            typeInfo);

        var decompressedBytes = GzipCompression.Decompress(compressedBytes);
        var json = Encoding.UTF8.GetString(decompressedBytes);
        var roundTrip = JsonSerializer.Deserialize(json, typeInfo);

        Assert.Equal(payload, roundTrip);
        Assert.Equal(decompressedBytes.Length, originalSizeBytes);
    }

    private static IReadOnlyList<FilterCandidate> InvokeFilter(
        MultiProjectionGrain grain,
        IReadOnlyList<FilterCandidate> events,
        Func<FilterCandidate, Guid> idSelector,
        Func<FilterCandidate, string> sortableIdSelector)
    {
        var method = typeof(MultiProjectionGrain)
            .GetMethod("FilterByPositionAndProcessed", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var genericMethod = method!.MakeGenericMethod(typeof(FilterCandidate));
        var result = genericMethod.Invoke(grain, new object[] { events, idSelector, sortableIdSelector });

        return Assert.IsAssignableFrom<IReadOnlyList<FilterCandidate>>(result);
    }

    private static void SetCatchUpCurrentPosition(MultiProjectionGrain grain, string? position)
    {
        var catchUpField = typeof(MultiProjectionGrain)
            .GetField("_catchUpProgress", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(catchUpField);

        var catchUpProgress = catchUpField!.GetValue(grain);
        Assert.NotNull(catchUpProgress);

        var currentPositionProperty = catchUpProgress!.GetType().GetProperty("CurrentPosition");
        Assert.NotNull(currentPositionProperty);
        currentPositionProperty!.SetValue(
            catchUpProgress,
            position is null ? null : new SortableUniqueId(position));
    }

    private sealed record FilterCandidate(Guid Id, string SortableUniqueIdValue);

    private sealed record CompressionPayload(string Name, int Count);
}

internal static class MultiProjectionGrainRetentionCompactionTestsAccessor
{
    public static MultiProjectionGrain CreateGrain()
    {
        var method = typeof(MultiProjectionGrainRetentionCompactionTests)
            .GetMethod("CreateGrain", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<MultiProjectionGrain>(method!.Invoke(null, null));
    }

    public static T GetField<T>(object target, string fieldName)
    {
        var method = typeof(MultiProjectionGrainRetentionCompactionTests)
            .GetMethod("GetField", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var genericMethod = method!.MakeGenericMethod(typeof(T));
        return Assert.IsType<T>(genericMethod.Invoke(null, new[] { target, fieldName }));
    }
}
