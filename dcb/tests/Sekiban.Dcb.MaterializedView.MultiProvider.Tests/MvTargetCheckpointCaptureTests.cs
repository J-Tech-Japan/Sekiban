using System.Data.Common;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

/// <summary>
///     Any failed read of the store head while capturing the target checkpoint is ReadUnavailable. No caller token
///     reaches that read, so a provider timeout surfacing as an OperationCanceledException is not cancellation.
/// </summary>
public class MvTargetCheckpointCaptureTests
{
    public static TheoryData<Exception> ReadFailures => new()
    {
        new TaskCanceledException("provider request timed out"),
        new OperationCanceledException("provider operation cancelled internally"),
        new IOException("store unavailable")
    };

    [Theory]
    [MemberData(nameof(ReadFailures))]
    public async Task FailedHeadRead_IsReadUnavailable(Exception failure)
    {
        var truth = await MvExecutorBase<DbConnection>.CaptureTargetCheckpointFromStoreAsync(new HeadStore(failure));

        Assert.False(truth.IsKnown);
        Assert.Equal(MvCheckpointUnknownReason.ReadUnavailable, truth.UnknownReason);
    }

    private sealed class HeadStore(Exception failure) : IEventStore
    {
        public Task<string> GetLatestSortableUniqueIdAsync() => Task.FromException<string>(failure);
        public Task<IEnumerable<TagStream>> ReadTagsAsync(ITag tag) => throw new NotSupportedException();
        public Task<TagState> GetLatestTagAsync(ITag tag) => throw new NotSupportedException();
        public Task<bool> TagExistsAsync(ITag tag) => throw new NotSupportedException();
        public Task<long> GetEventCountAsync(SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<IEnumerable<TagInfo>> GetAllTagsAsync(string? tagGroup = null) => throw new NotSupportedException();
        public Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            throw new NotSupportedException();
        public Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) =>
            throw new NotSupportedException();
        public Task<SerializableEvent?> ReadSerializableEventAsync(Guid eventId) => throw new NotSupportedException();
        public Task<IEnumerable<SerializableEvent>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
            throw new NotSupportedException();
        public Task<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)> WriteSerializableEventsAsync(
            IEnumerable<SerializableEvent> events) => throw new NotSupportedException();
    }
}
