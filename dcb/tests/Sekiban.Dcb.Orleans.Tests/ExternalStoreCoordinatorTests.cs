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
    public async Task Upsert_waits_for_a_parked_delete_and_runs_only_after_it_completes()
    {
        // The mirror of the test above. A reset's delete (InvalidateAsync) is in flight when an external-store
        // consumer — e.g. DeleteExternalStateAsync or a timer-issued snapshot upsert — arrives; the upsert must BLOCK on
        // the coordinator until the delete finishes, never run concurrently with it. This pins the serialization in both
        // directions, which is what routing EVERY external mutation (including the public delete) through the gate buys.
        var coordinator = new ExternalStoreCoordinator(() => false);
        var order = new List<string>();
        var deleteEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var deleteTask = coordinator.InvalidateAsync(async () =>
        {
            lock (order) order.Add("delete-start");
            deleteEntered.TrySetResult();
            await release.Task;
            lock (order) order.Add("delete-commit");
        });
        await deleteEntered.Task;

        var upsertTask = coordinator.UpsertAsync(() =>
        {
            lock (order) order.Add("upsert");
            return Task.FromResult(ResultBox.FromValue(true));
        });
        Assert.False(upsertTask.IsCompleted);
        lock (order) Assert.DoesNotContain("upsert", order);

        release.SetResult();
        await Task.WhenAll(deleteTask, upsertTask);
        Assert.Equal(new[] { "delete-start", "delete-commit", "upsert" }, order.ToArray());
    }

    [Fact]
    public async Task Upsert_while_faulted_returns_an_explicit_fault_blocked_error_without_touching_the_store()
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
        // A rejection is an explicit error carrying the stable fault-blocked exception — NOT a success carrying
        // false. This is what makes every caller inspecting only IsSuccess take the not-saved branch.
        Assert.False(blocked.IsSuccess);
        Assert.IsType<ExternalPersistenceBlockedByFaultException>(blocked.GetException());

        // Once the fault clears, the same upsert proceeds and succeeds.
        faulted = false;
        var allowed = await coordinator.UpsertAsync(() =>
        {
            invoked = true;
            return Task.FromResult(ResultBox.FromValue(true));
        });
        Assert.True(invoked);
        Assert.True(allowed.IsSuccess);
        Assert.True(allowed.GetValue());
    }
}
