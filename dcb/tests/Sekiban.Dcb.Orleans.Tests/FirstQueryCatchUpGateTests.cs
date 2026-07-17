using Sekiban.Dcb.Orleans.Grains;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Friend test for the production <see cref="FirstQueryCatchUpGate" /> — the fresh-activation first-query barrier's
///     single-flight core, driven DIRECTLY (not through Orleans call serialization). Multiple callers share one
///     in-flight work task: the work runs exactly once, every caller awaits the SAME task, and all observe the same
///     result or the same failure. A failure leaves the gate retryable (not satisfied); a success makes it sticky.
/// </summary>
public class FirstQueryCatchUpGateTests
{
    [Fact]
    public async Task Concurrent_first_callers_run_the_work_once_share_one_task_and_all_observe_success()
    {
        var gate = new FirstQueryCatchUpGate();
        gate.Arm();

        var runs = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Task> work = async () =>
        {
            Interlocked.Increment(ref runs);
            await release.Task;
        };

        // First caller invokes the work (which parks); later callers arrive while it is in flight.
        var first = gate.EnsureAsync(work);
        var second = gate.EnsureAsync(work);
        var third = gate.EnsureAsync(work);

        Assert.Equal(1, runs);           // the work ran exactly once for all three callers
        Assert.Same(first, second);      // all callers literally await the same in-flight task
        Assert.Same(first, third);
        Assert.True(gate.IsPending);     // still running, not yet satisfied

        release.SetResult();
        await Task.WhenAll(first, second, third);

        Assert.Equal(1, runs);
        Assert.False(gate.IsPending);    // satisfied

        // A later caller is a no-op — the work does not run again.
        await gate.EnsureAsync(work);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task Concurrent_first_callers_all_observe_the_same_failure_and_the_gate_stays_retryable()
    {
        var gate = new FirstQueryCatchUpGate();
        gate.Arm();

        var runs = 0;
        var boom = new InvalidOperationException("head read failed");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Task> failing = async () =>
        {
            Interlocked.Increment(ref runs);
            await release.Task;
            throw boom;
        };

        var first = gate.EnsureAsync(failing);
        var second = gate.EnsureAsync(failing);
        var third = gate.EnsureAsync(failing);
        Assert.Equal(1, runs);
        Assert.Same(first, second);
        Assert.Same(first, third);

        release.SetResult();

        foreach (var caller in new[] { first, second, third })
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => caller);
            Assert.Same(boom, ex); // every caller observes the SAME original exception
        }

        Assert.Equal(1, runs);       // ran once for the whole failed batch
        Assert.True(gate.IsPending); // NOT satisfied — a failure is retryable, nobody succeeded on empty state

        // A later caller retries the work; this time it succeeds and the gate becomes satisfied.
        Func<Task> ok = () =>
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        };
        await gate.EnsureAsync(ok);
        Assert.Equal(2, runs);        // retried
        Assert.False(gate.IsPending); // now satisfied
    }

    [Fact]
    public async Task Unarmed_gate_never_runs_the_work()
    {
        var gate = new FirstQueryCatchUpGate();
        var ran = false;

        var t = gate.EnsureAsync(() =>
        {
            ran = true;
            return Task.CompletedTask;
        });
        await t;

        Assert.True(t.IsCompletedSuccessfully);
        Assert.False(ran);
        Assert.False(gate.IsPending);
    }
}
