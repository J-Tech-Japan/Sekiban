using ResultBoxes;
using Sekiban.Dcb.Orleans.Grains;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Friend test for the production <see cref="ExternalStoreCoordinator" /> — the activation-local gate that
///     serialises external derived-snapshot upserts and the reset's delete, and rejects an upsert while faulted. Drives
///     it directly (no Orleans): a parked upsert makes the delete WAIT and run only after the upsert commits (so no
///     stale upsert can recreate what the delete removes), and a faulted upsert is skipped without touching the store.
/// </summary>
public class ExternalStoreCoordinatorTests
{
    [Fact]
    public async Task Delete_waits_for_a_parked_upsert_and_runs_only_after_it_commits()
    {
        var coordinator = new ExternalStoreCoordinator(() => false);
        var order = new List<string>();
        var upsertEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // An upsert enters and parks inside the coordinator (as an interleaving timer's in-flight upsert would).
        var upsertTask = coordinator.UpsertAsync(async () =>
        {
            lock (order) order.Add("upsert-start");
            upsertEntered.TrySetResult();
            await release.Task;
            lock (order) order.Add("upsert-commit");
            return ResultBox.FromValue(true);
        });
        await upsertEntered.Task;

        // The reset's delete is requested while the upsert is parked. It must BLOCK on the coordinator, not run.
        var deleteTask = coordinator.InvalidateAsync(() =>
        {
            lock (order) order.Add("delete");
            return Task.CompletedTask;
        });
        Assert.False(deleteTask.IsCompleted);
        lock (order) Assert.DoesNotContain("delete", order);

        // Release the parked upsert: it commits, THEN the delete runs — never concurrently, never before.
        release.SetResult();
        await Task.WhenAll(upsertTask, deleteTask);
        Assert.Equal(new[] { "upsert-start", "upsert-commit", "delete" }, order.ToArray());
    }

    [Fact]
    public async Task Upsert_is_skipped_while_faulted_without_touching_the_store()
    {
        var faulted = true;
        var coordinator = new ExternalStoreCoordinator(() => faulted);
        var invoked = false;

        var blocked = await coordinator.UpsertAsync(() =>
        {
            invoked = true;
            return Task.FromResult(ResultBox.FromValue(true));
        });
        Assert.False(invoked);              // the store delegate was never called
        Assert.False(blocked.GetValue());   // reported as not-persisted (a skip, not an error)

        // Once the fault clears, the same upsert proceeds.
        faulted = false;
        var allowed = await coordinator.UpsertAsync(() =>
        {
            invoked = true;
            return Task.FromResult(ResultBox.FromValue(true));
        });
        Assert.True(invoked);
        Assert.True(allowed.GetValue());
    }
}
