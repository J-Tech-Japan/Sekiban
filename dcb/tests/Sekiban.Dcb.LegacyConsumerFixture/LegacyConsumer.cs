using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.LegacyConsumerFixture;

/// <summary>
///     A downstream consumer written BEFORE SEK-G15, compiled against the produced Sekiban assemblies. Everything here
///     uses only pre-SEK-G15 public surfaces. It exists to fail the build if any of those surfaces changed.
/// </summary>
public static class LegacyConsumer
{
    /// <summary>Uses the legacy executor overloads and ICommandContext.AppendEvent exactly as before SEK-G15.</summary>
    public static async Task<ResultBox<ExecutionResult>> ExecuteLegacyCommand(
        ISekibanExecutor executor,
        LegacyCommand command) =>
        await executor.ExecuteAsync(
            command,
            (LegacyCommand _, ICommandContext ctx) => ctx.AppendEvent(new LegacyEvent("x"), new LegacyTag("id")));

    /// <summary>Constructs the legacy positional serialized-commit DTO exactly as before SEK-G15.</summary>
    public static SerializedCommitRequest BuildLegacySerializedCommit() =>
        new(
            new List<SerializableEventCandidate>
            {
                new(new byte[] { 1 }, nameof(LegacyEvent), new List<string> { "Legacy:id" })
            },
            new List<ConsistencyTagEntry>());

    public record LegacyCommand : ICommand;

    public record LegacyEvent(string Value) : IEventPayload;

    public record LegacyTag(string Id) : IStringTagGroup<LegacyTag>
    {
        public static string TagGroupName => "Legacy";
        public static LegacyTag FromContent(string content) => new(content);
        public bool IsConsistencyTag() => false;
        public string GetId() => Id;
    }
}

/// <summary>
///     A hand-rolled external event store representing a downstream implementor (e.g. WasmRuntime or SekibanAsAService's
///     ServicePartitionedEventStore) that implements ONLY <see cref="IEventStore" />. That this still compiles against the
///     produced assemblies — with no new abstract members forced onto it — is the compatibility guarantee.
/// </summary>
public sealed class LegacyExternalEventStore : IEventStore
{
    public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
        WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) =>
        Task.FromResult(
            ResultBox.FromValue(
                ((IReadOnlyList<SerializableEvent>)events.ToList(),
                    (IReadOnlyList<TagWriteResult>)new List<TagWriteResult>())));

    public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) =>
        Task.FromResult(ResultBox.FromValue(Enumerable.Empty<TagStream>()));
    public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => throw new NotSupportedException();
    public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => Task.FromResult(ResultBox.FromValue(false));
    public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) =>
        Task.FromResult(ResultBox.FromValue(0L));
    public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) =>
        Task.FromResult(ResultBox.FromValue(Enumerable.Empty<TagInfo>()));
    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
        Task.FromResult(ResultBox.FromValue(Enumerable.Empty<SerializableEvent>()));
    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) =>
        Task.FromResult(ResultBox.FromValue(Enumerable.Empty<SerializableEvent>()));
    public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) => throw new NotSupportedException();
    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
        Task.FromResult(ResultBox.FromValue(Enumerable.Empty<SerializableEvent>()));
    public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
        Task.FromResult(ResultBox.FromValue(string.Empty));
}
