using ResultBoxes;
using Sekiban.Dcb.Boundaries;
using Xunit;
namespace Sekiban.Dcb.WithoutResult.Tests.Boundaries;

/// <summary>
///     A failure reaches a boundary two ways: carried inside the box, or thrown by the Task that was supposed to
///     produce the box. To the caller they are the same failure, so they must be treated the same — original
///     exception, original stack, and the boundary it crossed. Awaiting the Task naively would have let the second
///     kind through unannotated.
/// </summary>
public class TaskCarriedFailureTests
{
    private static readonly BoundaryContext Context = new("ICommandContext.TagExistsAsync", "Student:1");

    [Fact]
    public async Task FaultedTask_RethrowsTheOriginalInstance_AndNamesTheBoundary()
    {
        var failure = new InvalidOperationException("the task itself blew up");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => GuardedUnwrap.UnwrapAsync(Task.FromException<ResultBox<bool>>(failure), Context));

        Assert.Same(failure, thrown);
        Assert.Equal("ICommandContext.TagExistsAsync", thrown.Data[GuardedUnwrap.OperationDataKey]);
        Assert.Equal("Student:1", thrown.Data[GuardedUnwrap.TargetDataKey]);
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
            () => GuardedUnwrap.UnwrapAsync(Task.FromException<ResultBox<string>>(failure), Context));

        Assert.Contains(firstFrame, thrown.StackTrace);
    }

    [Fact]
    public async Task FaultedTask_CarryingCancellation_KeepsItsTypeAndToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancellation = new OperationCanceledException("cancelled", cts.Token);

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(
            () => GuardedUnwrap.UnwrapAsync(Task.FromException<ResultBox<bool>>(cancellation), Context));

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
            () => GuardedUnwrap.UnwrapAsync(Task.FromCanceled<ResultBox<bool>>(cts.Token), Context));

        Assert.Equal(cts.Token, thrown.CancellationToken);
        Assert.IsNotType<SekibanBoundaryException>(thrown);
        Assert.Equal("ICommandContext.TagExistsAsync", thrown.Data[GuardedUnwrap.OperationDataKey]);
    }

    [Fact]
    public async Task FaultedTask_AtAValueTypedBoundary_DoesNotBecomeDefault()
    {
        // The whole point, restated for the Task path: a bool-returning boundary must not answer `false` because
        // the call blew up.
        var failure = new InvalidOperationException("event store unreachable");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => GuardedUnwrap.UnwrapAsync(Task.FromException<ResultBox<bool>>(failure), Context));
    }

    [Fact]
    public async Task SuccessfulTask_IsUntouched()
    {
        var value = await GuardedUnwrap.UnwrapAsync(Task.FromResult(ResultBox.FromValue(true)), Context);
        Assert.True(value);
    }
}
