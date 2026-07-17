using Sekiban.Dcb.Orleans.Grains;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Friend test for the production <see cref="GrainStateWriteCoordinator" /> — the single-writer coordinator every
///     one of the grain's audited state-write call sites routes through. It competes two DIFFERENT representative
///     operations (a normal checkpoint write and a fault-descriptor retry write): the first is parked inside the
///     storage delegate while the second becomes ready, proving the coordinator (not the harness) serializes them.
///     Asserts max one concurrent write, start/commit order, monotonic version, and that the final persisted state
///     carries the fault descriptor while retaining the checkpoint (no overwrite). Bypassing the coordinator makes the
///     writes overlap and fails these assertions.
/// </summary>
public class GrainStateWriteCoordinatorTests
{
    [Fact]
    public async Task Checkpoint_then_fault_retry_writes_are_serialized_with_no_overwrite()
    {
        await RunCompetingWritesAsync(faultFirst: false);
    }

    [Fact]
    public async Task Fault_retry_then_checkpoint_writes_are_serialized_with_no_overwrite()
    {
        // Inverse ordering — the coordinator is symmetric, so the same guarantees must hold either way.
        await RunCompetingWritesAsync(faultFirst: true);
    }

    private static async Task RunCompetingWritesAsync(bool faultFirst)
    {
        var state = new ModelState();
        var store = new ParkingStore();
        var coordinator = new GrainStateWriteCoordinator(() => store.PersistAsync(state));

        // First operation: mutate its field, then write. It enters the store and parks (holding the coordinator).
        if (faultFirst)
        {
            state.FaultDescriptor = "fault";
        }
        else
        {
            state.Checkpoint = "cp";
        }

        var first = coordinator.WriteAsync();
        await store.FirstEntered;

        // Second operation becomes ready while the first is parked inside the store.
        if (faultFirst)
        {
            state.Checkpoint = "cp";
        }
        else
        {
            state.FaultDescriptor = "fault";
        }

        var second = coordinator.WriteAsync();

        // The second must be WAITING on the coordinator, not inside the store: only one write in flight.
        Assert.False(second.IsCompleted);
        Assert.Equal(1, store.MaxConcurrent);

        store.Release();
        await Task.WhenAll(first, second);

        Assert.Equal(1, store.MaxConcurrent);                                   // never overlapped
        Assert.Equal(new[] { 1, 2 }, store.CommitVersions.ToArray());           // monotonic version, first then second
        var final = store.LastCommit;
        Assert.Equal("cp", final.Checkpoint);                                   // checkpoint retained
        Assert.Equal("fault", final.FaultDescriptor);                          // fault descriptor present
    }

    [Fact]
    public void All_grain_state_writes_route_through_the_coordinator_seven_call_site_audit()
    {
        var source = ReadGrainSource();

        // Every state write must go through the gated writer; the ONLY direct _state.WriteStateAsync() is the delegate
        // handed to the coordinator in the constructor. A new call site that writes state directly would bypass the
        // single-writer serialization — this audit fails if one is introduced.
        var directWrites = CountOccurrences(source, "_state.WriteStateAsync()");
        Assert.Equal(1, directWrites); // the coordinator delegate only

        Assert.Contains("new GrainStateWriteCoordinator(() => _state.WriteStateAsync())", source);

        // The seven audited call sites all route through the coordinator via WriteGrainStateGatedAsync().
        var gatedCallSites = CountOccurrences(source, "await WriteGrainStateGatedAsync()");
        Assert.Equal(7, gatedCallSites);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string ReadGrainSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "dcb",
                "src",
                "Sekiban.Dcb.Orleans.Core",
                "Grains",
                "MultiProjectionGrain.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate MultiProjectionGrain.cs by walking up from the test output directory.");
    }

    private sealed class ModelState
    {
        public string? Checkpoint;
        public string? FaultDescriptor;
    }

    /// <summary>
    ///     Models the grain's persistent store: records the maximum number of concurrent writes and a monotonic version
    ///     per commit, and PARKS the first write so a second (if it were allowed to overlap) would be observed. The
    ///     harness itself imposes no serialization — the coordinator must.
    /// </summary>
    private sealed class ParkingStore
    {
        private readonly object _sync = new();
        private int _current;
        public int MaxConcurrent;
        private int _version;
        public readonly List<int> CommitVersions = new();
        private readonly List<(string? Checkpoint, string? FaultDescriptor)> _commits = new();
        private readonly TaskCompletionSource _firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstEntered => _firstEntered.Task;
        public void Release() => _release.TrySetResult();
        public (string? Checkpoint, string? FaultDescriptor) LastCommit => _commits[^1];

        public async Task PersistAsync(ModelState state)
        {
            int n;
            lock (_sync)
            {
                n = ++_current;
                if (n > MaxConcurrent)
                {
                    MaxConcurrent = n;
                }
            }

            if (n == 1)
            {
                _firstEntered.TrySetResult();
                await _release.Task; // park the first writer so a bypassed second write would overlap it here
            }

            lock (_sync)
            {
                _version++;
                CommitVersions.Add(_version);
                _commits.Add((state.Checkpoint, state.FaultDescriptor));
                _current--;
            }
        }
    }
}
