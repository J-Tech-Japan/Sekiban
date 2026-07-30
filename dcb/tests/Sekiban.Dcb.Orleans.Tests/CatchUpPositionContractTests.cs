using Sekiban.Dcb.Common;
using Sekiban.Dcb.Orleans.Grains;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Killing proofs for the G21 START/REACHED contract split. START has one restored-position owner; REACHED evidence
///     is carried by the invocation result rather than obtained from mutable shared progress.
/// </summary>
public class CatchUpPositionContractTests
{
    private static SortableUniqueId Position(long tick) =>
        new(SortableUniqueId.GetTickString(tick) + SortableUniqueId.GetIdString(Guid.Empty));

    [Fact]
    public async Task Restored_start_wins_over_host_inference_and_is_consumed_exactly_once()
    {
        var resolver = new CatchUpStartPositionLeaseResolver();
        var restored = Position(20);
        var inferred = Position(10);
        var inferenceCalls = 0;
        resolver.Restore(restored);

        var first = await resolver.AcquireAsync(
            forceFullReplay: false,
            () =>
            {
                inferenceCalls++;
                return Task.FromResult<SortableUniqueId?>(inferred);
            });
        var second = await resolver.AcquireAsync(
            forceFullReplay: false,
            () =>
            {
                inferenceCalls++;
                return Task.FromResult<SortableUniqueId?>(inferred);
            });

        Assert.Equal(CatchUpStartPositionSource.RestoredCheckpoint, first.Source);
        Assert.Equal(restored.Value, first.StartPosition?.Value);
        Assert.Equal(CatchUpStartPositionSource.InferredCheckpoint, second.Source);
        Assert.Equal(inferred.Value, second.StartPosition?.Value);
        Assert.Equal(1, inferenceCalls);
    }

    [Fact]
    public async Task Racing_paths_can_lease_the_restored_start_only_once()
    {
        var resolver = new CatchUpStartPositionLeaseResolver();
        var restored = Position(30);
        var inferred = Position(5);
        resolver.Restore(restored);

        var leases = await Task.WhenAll(
            resolver.AcquireAsync(false, () => Task.FromResult<SortableUniqueId?>(inferred)),
            resolver.AcquireAsync(false, () => Task.FromResult<SortableUniqueId?>(inferred)));

        Assert.Single(leases, lease => lease.Source == CatchUpStartPositionSource.RestoredCheckpoint);
        Assert.Single(leases, lease => lease.Source == CatchUpStartPositionSource.InferredCheckpoint);
        Assert.Contains(leases, lease => lease.StartPosition?.Value == restored.Value);
    }

    [Fact]
    public async Task Empty_restored_record_is_present_and_leased_once_without_host_inference()
    {
        var resolver = new CatchUpStartPositionLeaseResolver();
        var inferred = Position(5);
        var inferenceCalls = 0;
        resolver.Restore(null);

        var restored = await resolver.AcquireAsync(
            false,
            () =>
            {
                inferenceCalls++;
                return Task.FromResult<SortableUniqueId?>(inferred);
            });
        var next = await resolver.AcquireAsync(
            false,
            () =>
            {
                inferenceCalls++;
                return Task.FromResult<SortableUniqueId?>(inferred);
            });

        Assert.Equal(CatchUpStartPositionSource.RestoredCheckpoint, restored.Source);
        Assert.Null(restored.StartPosition);
        Assert.Equal(CatchUpStartPositionSource.InferredCheckpoint, next.Source);
        Assert.Equal(inferred.Value, next.StartPosition?.Value);
        Assert.Equal(1, inferenceCalls);
    }

    [Fact]
    public void Reached_cursor_is_invocation_owned_and_distinct_from_start()
    {
        var start = new CatchUpStartPositionLease(Position(40), CatchUpStartPositionSource.RestoredCheckpoint);
        var reached = Position(41);

        var result = new CatchUpInvocationResult(start, reached);

        Assert.Equal(Position(40).Value, result.Start.StartPosition?.Value);
        Assert.Equal(reached.Value, result.AuthoritativeReachedPosition?.Value);
    }

    [Fact]
    public async Task Parked_timer_then_in_call_run_has_one_writer_and_preserves_in_call_cursor()
    {
        var gate = new CatchUpRunExecutionGate();
        var timerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTimer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inCallEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cursor = Position(50);
        var writers = 0;
        var maxWriters = 0;

        var timer = RunAsync(timerEntered, releaseTimer.Task, Position(51));
        await timerEntered.Task;
        var inCall = RunAsync(inCallEntered, Task.CompletedTask, Position(52));
        Assert.False(inCallEntered.Task.IsCompleted);

        releaseTimer.SetResult();
        await Task.WhenAll(timer, inCall);

        Assert.Equal(1, maxWriters);
        Assert.Equal(Position(52).Value, cursor.Value);

        async Task RunAsync(TaskCompletionSource entered, Task release, SortableUniqueId write)
        {
            await using (await gate.EnterAsync())
            {
                var active = Interlocked.Increment(ref writers);
                maxWriters = Math.Max(maxWriters, active);
                entered.SetResult();
                await release;
                cursor = write;
                Interlocked.Decrement(ref writers);
            }
        }
    }

    [Fact]
    public async Task In_call_then_superseded_timer_cannot_overwrite_the_in_call_cursor()
    {
        var gate = new CatchUpRunExecutionGate();
        var invocationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInvocation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduledRun = new object();
        var currentRun = scheduledRun;
        var cursor = Position(60);

        var invocation = Task.Run(
            async () =>
            {
                await using (await gate.EnterAsync())
                {
                    invocationEntered.SetResult();
                    await releaseInvocation.Task;
                    cursor = Position(62);
                    currentRun = new object();
                }
            });
        await invocationEntered.Task;

        var timer = Task.Run(
            async () =>
            {
                await using (await gate.EnterAsync())
                {
                    timerEntered.SetResult();
                    if (ReferenceEquals(currentRun, scheduledRun))
                    {
                        cursor = Position(61);
                    }
                }
            });
        Assert.False(timerEntered.Task.IsCompleted);

        releaseInvocation.SetResult();
        await Task.WhenAll(invocation, timer);

        Assert.Equal(Position(62).Value, cursor.Value);
    }
}
