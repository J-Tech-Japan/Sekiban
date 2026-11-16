using Sekiban.Dcb.Boundaries;
using Xunit;
namespace Sekiban.Dcb.WithoutResult.Tests.Boundaries;

/// <summary>
///     A failure can reach a boundary as a faulted or cancelled Task. It must arrive as itself — original exception,
///     original stack — carrying the boundary it crossed. Awaiting the Task naively would let it through unannotated.
/// </summary>
public class TaskCarriedFailureTests
{
    private static readonly BoundaryContext Context = new("ICommandContext.TagExistsAsync", "Student:1");

    [Fact]
    public async Task FaultedTask_RethrowsTheOriginalInstance_AndNamesTheBoundary()
    {
        var failure = new InvalidOperationException("the task itself blew up");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BoundaryGuard.AwaitAsync(Task.FromException<bool>(failure), Context));

        Assert.Same(failure, thrown);
        Assert.Equal("ICommandContext.TagExistsAsync", thrown.Data[BoundaryGuard.OperationDataKey]);
        Assert.Equal("Student:1", thrown.Data[BoundaryGuard.TargetDataKey]);
    }

    [Fact]
    public async Task FaultedTask_PreservesTheOriginalStack()
    {
        Exception failure;
        try
        {
            throw new InvalidOperationException("thrown somewhere real");
        }
        catch (InvalidOperationException caught)
        {
            failure = caught;
        }

        var firstFrame = failure.StackTrace!.Trim().Split('\n')[0].Trim();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BoundaryGuard.AwaitAsync(Task.FromException<string>(failure), Context));

        Assert.Contains(firstFrame, thrown.StackTrace);
    }

    [Fact]
    public async Task FaultedTask_CarryingCancellation_KeepsItsTypeAndToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancellation = new OperationCanceledException("cancelled", cts.Token);

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(
            () => BoundaryGuard.AwaitAsync(Task.FromException<bool>(cancellation), Context));

        Assert.Same(cancellation, thrown);
        Assert.Equal(cts.Token, thrown.CancellationToken);
        Assert.IsNotType<SekibanBoundaryException>(thrown);
    }

    [Fact]
    public async Task CancelledTask_StaysCancellation_WithItsToken_AndNamesTheBoundary()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // A Task cancelled by the runtime, not one faulted with an exception we constructed: the runtime creates the
        // TaskCanceledException itself, so identity is not ours to assert — the type, the token and the annotation
        // are.
        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => BoundaryGuard.AwaitAsync(Task.FromCanceled<bool>(cts.Token), Context));

        Assert.Equal(cts.Token, thrown.CancellationToken);
        Assert.IsNotType<SekibanBoundaryException>(thrown);
        Assert.Equal("ICommandContext.TagExistsAsync", thrown.Data[BoundaryGuard.OperationDataKey]);
    }

    [Fact]
    public async Task FaultedTask_AtAValueTypedBoundary_DoesNotBecomeDefault()
    {
        // The whole point, restated for the Task path: a bool-returning boundary must not answer `false` because
        // the call blew up.
        var failure = new InvalidOperationException("event store unreachable");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BoundaryGuard.AwaitAsync(Task.FromException<bool>(failure), Context));
    }

    [Fact]
    public async Task SuccessfulTask_IsUntouched()
    {
        var value = await BoundaryGuard.AwaitAsync(Task.FromResult(true), Context);
        Assert.True(value);
    }
}
