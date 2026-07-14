using ResultBoxes;
using Sekiban.Dcb.Boundaries;
using System.Collections;
using Xunit;
namespace Sekiban.Dcb.WithoutResult.Tests.Boundaries;

/// <summary>
///     One exception instance can legitimately reach several boundaries at once — a shared storage failure observed
///     by concurrent in-flight tasks, or one cached failure handed to every waiter. <see cref="Exception.Data" /> is
///     a plain <see cref="IDictionary" /> with no thread-safety promise, and the operation and the target are one
///     context: a pair naming operation A and target B would be a lie the reader has no way to detect.
/// </summary>
public class ConcurrentAnnotationTests
{
    private static readonly BoundaryContext Inner = new("ICommandContext.TagExistsAsync", "target-for-TagExistsAsync");
    private static readonly BoundaryContext Outer = new("ISekibanExecutor.ExecuteAsync", "target-for-ExecuteAsync");

    /// <summary>
    ///     The deterministic one. Racing two threads and hoping they interleave proves nothing — I tried it, and it
    ///     passed against a version of the guard with the lock removed, three runs out of three. So this test does
    ///     not hope: the exception's own <c>Data</c> dictionary parks the first writer INSIDE its write, and only
    ///     lets it finish once a second thread has got past the <c>Contains</c> check. If the read and the writes are
    ///     not one atomic step, the second thread gets past that check and annotates too — and the write counter says
    ///     so, every time. If they ARE atomic, the second thread cannot even reach the check while the first holds
    ///     the gate, so it waits, then finds the annotation already there and leaves it alone: exactly one write.
    /// </summary>
    [Fact]
    public void AnnotatingTheSameExceptionFromTwoBoundariesAtOnce_WritesTheContextExactlyOnce()
    {
        var data = new GatedData();
        var failure = new GatedDataException("event store unreachable", data);
        var box = ResultBox<bool>.Error(failure);

        var first = RunOnItsOwnThread(() => Assert.Throws<GatedDataException>(() => GuardedUnwrap.Unwrap(box, Inner)));
        var second = RunOnItsOwnThread(() => Assert.Throws<GatedDataException>(() => GuardedUnwrap.Unwrap(box, Outer)));

        Assert.True(Task.WaitAll([first, second], TimeSpan.FromSeconds(20)), "the annotation deadlocked");

        // One boundary annotated. The other found the annotation there and did not touch it.
        Assert.Equal(1, data.OperationWrites);
        Assert.Equal(1, data.TargetWrites);

        // And the pair it left describes ONE boundary, not one field from each.
        var operation = Assert.IsType<string>(failure.Data[GuardedUnwrap.OperationDataKey]);
        var target = Assert.IsType<string>(failure.Data[GuardedUnwrap.TargetDataKey]);
        Assert.Equal($"target-for-{operation.Split('.')[1]}", target);
    }

    /// <summary>
    ///     The same race through the Task path rather than the box path — a faulted Task whose exception instance is
    ///     shared by every waiter.
    /// </summary>
    [Fact]
    public void AFaultedTaskSharedByTwoBoundaries_AlsoWritesTheContextExactlyOnce()
    {
        var data = new GatedData();
        var failure = new GatedDataException("the task itself blew up", data);

        var first = RunOnItsOwnThread(() => Assert.ThrowsAsync<GatedDataException>(
                () => GuardedUnwrap.UnwrapAsync(Task.FromException<ResultBox<bool>>(failure), Inner))
            .GetAwaiter()
            .GetResult());
        var second = RunOnItsOwnThread(() => Assert.ThrowsAsync<GatedDataException>(
                () => GuardedUnwrap.UnwrapAsync(Task.FromException<ResultBox<bool>>(failure), Outer))
            .GetAwaiter()
            .GetResult());

        Assert.True(Task.WaitAll([first, second], TimeSpan.FromSeconds(20)), "the annotation deadlocked");

        Assert.Equal(1, data.OperationWrites);
        Assert.Equal(1, data.TargetWrites);
    }

    [Fact]
    public void AnExceptionWithReadOnlyData_StillArrivesAsItself()
    {
        // Losing a diagnostic annotation must never cost the caller the real failure.
        var failure = new ReadOnlyDataException("cannot annotate me");

        var thrown = Assert.Throws<ReadOnlyDataException>(
            () => GuardedUnwrap.Unwrap(ResultBox<bool>.Error(failure), Inner));

        Assert.Same(failure, thrown);
    }

    /// <summary>A real thread, not a pool work item: the schedule below must not wait on thread injection.</summary>
    private static Task RunOnItsOwnThread(Action action) =>
        Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    /// <summary>
    ///     An <see cref="Exception.Data" /> that forces the interleaving instead of waiting for one.
    ///     The first thread to write the operation key is parked inside that write until a second thread has passed
    ///     <see cref="Contains" /> — which it can only do if the guard's read-then-write is not atomic. It counts the
    ///     writes so the test can tell exactly what happened.
    /// </summary>
    private sealed class GatedData : IDictionary
    {
        private readonly Hashtable _entries = [];
        private readonly ManualResetEventSlim _secondReaderPassedContains = new(false);
        private int _containsCalls;
        private int _operationWrites;
        private int _targetWrites;

        public int OperationWrites => Volatile.Read(ref _operationWrites);
        public int TargetWrites => Volatile.Read(ref _targetWrites);

        public bool Contains(object key)
        {
            var present = _entries.Contains(key);

            if (Equals(key, GuardedUnwrap.OperationDataKey) && Interlocked.Increment(ref _containsCalls) >= 2)
            {
                // A second boundary got as far as the check. Under an atomic annotation this can only happen after
                // the first one has completely finished — which is precisely what we are testing.
                _secondReaderPassedContains.Set();
            }

            return present;
        }

        public object? this[object key]
        {
            get => _entries[key];
            set
            {
                if (Equals(key, GuardedUnwrap.OperationDataKey))
                {
                    if (Interlocked.Increment(ref _operationWrites) == 1)
                    {
                        // Park mid-write. If the guard is atomic, nobody else can reach Contains, and we time out and
                        // carry on — the test still completes, and the counter still tells the truth.
                        _secondReaderPassedContains.Wait(TimeSpan.FromSeconds(2));
                    }
                }
                else if (Equals(key, GuardedUnwrap.TargetDataKey))
                {
                    Interlocked.Increment(ref _targetWrites);
                }

                _entries[key] = value;
            }
        }

        public bool IsReadOnly => false;
        public bool IsFixedSize => false;
        public int Count => _entries.Count;
        public bool IsSynchronized => false;
        public object SyncRoot => _entries.SyncRoot;
        public ICollection Keys => _entries.Keys;
        public ICollection Values => _entries.Values;
        public void Add(object key, object? value) => this[key] = value;
        public void Clear() => _entries.Clear();
        public void Remove(object key) => _entries.Remove(key);
        public IDictionaryEnumerator GetEnumerator() => _entries.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();
        public void CopyTo(Array array, int index) => _entries.CopyTo(array, index);
    }

    private sealed class GatedDataException(string message, GatedData data) : Exception(message)
    {
        public override IDictionary Data { get; } = data;
    }

    /// <summary>An exception whose <c>Data</c> refuses writes — the case the guard must survive, not propagate.</summary>
    private sealed class ReadOnlyDataException(string message) : Exception(message)
    {
        public override IDictionary Data { get; } =
            new System.Collections.ObjectModel.ReadOnlyDictionary<object, object?>(new Dictionary<object, object?>());
    }
}
