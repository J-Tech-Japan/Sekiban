using Xunit;
using System.Reflection;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     The tag-state grain reads the consistent tag's latest position through the Orleans actor wrapper. A failure the
///     tag-consistent grain reports (its try-read returns <c>null</c>) means "unknown latest"; a failed grain call (a
///     timeout or transport fault) must propagate instead of being read as unknown.
/// </summary>
public class TagConsistentLatestPositionReaderTests
{
    [Fact]
    public async Task GrainReportedFailure_IsUnknownLatest()
    {
        var actor = await ResolveActorAsync(() => Task.FromResult<string?>(null));

        Assert.Null(await TagConsistentLatestPositionReader.TryReadAsync(actor));
    }

    [Fact]
    public async Task FailedGrainCall_Propagates()
    {
        var transport = new TimeoutException("grain call timed out");
        var actor = await ResolveActorAsync(() => Task.FromException<string?>(transport));

        var thrown = await Assert.ThrowsAsync<TimeoutException>(() => TagConsistentLatestPositionReader.TryReadAsync(actor));
        Assert.Same(transport, thrown);
    }

    [Fact]
    public async Task GrainValue_IsReturned()
    {
        var actor = await ResolveActorAsync(() => Task.FromResult<string?>("063930412334851845000116174724"));

        Assert.Equal("063930412334851845000116174724", await TagConsistentLatestPositionReader.TryReadAsync(actor));
    }

    private static Task<ITagConsistentActorCommon> ResolveActorAsync(Func<Task<string?>> latest)
    {
        var grain = DispatchProxy.Create<ITagConsistentGrain, LatestGrain>();
        ((LatestGrain)(object)grain).Latest = latest;
        var cluster = DispatchProxy.Create<IClusterClient, SingleGrainClusterClient>();
        ((SingleGrainClusterClient)(object)cluster).Grain = grain;
        var accessor = new OrleansActorObjectAccessor(cluster, null!, null!);
        return accessor.GetActorAsync<ITagConsistentActorCommon>("Student:1");
    }

    public class LatestGrain : DispatchProxy
    {
        public Func<Task<string?>> Latest { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(ITagConsistentGrain.TryGetLatestSortableUniqueIdAsync)
                ? Latest()
                : throw new NotSupportedException(targetMethod?.Name);
    }

    public class SingleGrainClusterClient : DispatchProxy
    {
        public ITagConsistentGrain Grain { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IGrainFactory.GetGrain)
                ? Grain
                : throw new NotSupportedException(targetMethod?.Name);
    }
}
