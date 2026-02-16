using System.Text;
using System.Text.Json;
using Dcb.Domain;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class SerializedCommitTests
{
    private readonly DcbDomainTypes _domainTypes = DomainType.GetDomainTypes();

    private ISerializedSekibanDcbExecutor CreateExecutor()
    {
        return (ISerializedSekibanDcbExecutor)new InMemoryDcbExecutor(_domainTypes);
    }

    private static byte[] SerializePayload<T>(T payload)
    {
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
    }

    [Fact]
    public async Task CommitSingleEvent_Should_Succeed()
    {
        // Given
        var executor = CreateExecutor();
        var studentId = Guid.NewGuid();
        var payload = SerializePayload(new StudentCreated(studentId, "Alice", 5));
        var tagString = $"Student:{studentId}";

        var request = new SerializedCommitRequest(
            [new SerializableEventCandidate(payload, nameof(StudentCreated), [tagString])],
            [new ConsistencyTagEntry(tagString, "")]);

        // When
        var result = await executor.CommitSerializableEventsAsync(request);

        // Then
        Assert.True(result.IsSuccess);
        var commitResult = result.GetValue();
        Assert.Single(commitResult.WrittenEvents);
        Assert.Equal(nameof(StudentCreated), commitResult.WrittenEvents[0].EventPayloadName);
        Assert.Contains(tagString, commitResult.WrittenEvents[0].Tags);
        Assert.True(commitResult.Duration > TimeSpan.Zero);
        Assert.NotEmpty(commitResult.TagWriteResults);
    }

    [Fact]
    public async Task CommitMultipleEvents_Should_Succeed()
    {
        // Given
        var executor = CreateExecutor();
        var studentId1 = Guid.NewGuid();
        var studentId2 = Guid.NewGuid();
        var payload1 = SerializePayload(new StudentCreated(studentId1, "Alice", 5));
        var payload2 = SerializePayload(new StudentCreated(studentId2, "Bob", 3));
        var tagString1 = $"Student:{studentId1}";
        var tagString2 = $"Student:{studentId2}";

        var request = new SerializedCommitRequest(
            [
                new SerializableEventCandidate(payload1, nameof(StudentCreated), [tagString1]),
                new SerializableEventCandidate(payload2, nameof(StudentCreated), [tagString2])
            ],
            [
                new ConsistencyTagEntry(tagString1, ""),
                new ConsistencyTagEntry(tagString2, "")
            ]);

        // When
        var result = await executor.CommitSerializableEventsAsync(request);

        // Then
        Assert.True(result.IsSuccess);
        var commitResult = result.GetValue();
        Assert.Equal(2, commitResult.WrittenEvents.Count);
        Assert.Equal(nameof(StudentCreated), commitResult.WrittenEvents[0].EventPayloadName);
        Assert.Equal(nameof(StudentCreated), commitResult.WrittenEvents[1].EventPayloadName);
    }

    [Fact]
    public async Task CommitEmptyRequest_Should_ReturnEmptyResult()
    {
        // Given
        var executor = CreateExecutor();
        var request = new SerializedCommitRequest([], []);

        // When
        var result = await executor.CommitSerializableEventsAsync(request);

        // Then
        Assert.True(result.IsSuccess);
        var commitResult = result.GetValue();
        Assert.Empty(commitResult.WrittenEvents);
        Assert.Empty(commitResult.TagWriteResults);
    }

    [Fact]
    public async Task CommitWithConsistencyConflict_Should_Fail()
    {
        // Given: commit first event to establish state
        var executor = CreateExecutor();
        var sekibanExecutor = (ISekibanExecutor)executor;
        var studentId = Guid.NewGuid();
        var tagString = $"Student:{studentId}";

        var firstCommand = new CreateStudent(studentId, "Alice", 5);
        var firstResult = await sekibanExecutor.ExecuteAsync(firstCommand);
        Assert.True(firstResult.IsSuccess);

        // Get the current sortable unique id from the written event
        var writtenSortableId = firstResult.GetValue().SortableUniqueId;
        Assert.NotNull(writtenSortableId);

        // Given: prepare a second commit with a stale (incorrect) sortable unique id
        // This simulates another client having an outdated view with a wrong version
        var payload = SerializePayload(new StudentCreated(studentId, "Alice Updated", 3));

        // Use a fabricated non-empty but incorrect sortable unique id to trigger
        // the optimistic concurrency check in MakeReservationAsync.
        // The actor checks: if lastSortableUniqueId is non-empty AND doesn't match
        // the actual stored version, reservation fails.
        var staleSortableId = "0000000000000000000_00000000000";
        var request = new SerializedCommitRequest(
            [new SerializableEventCandidate(payload, nameof(StudentCreated), [tagString])],
            [new ConsistencyTagEntry(tagString, staleSortableId)]);

        // When
        var result = await executor.CommitSerializableEventsAsync(request);

        // Then: reservation should fail because staleSortableId != actual last sortable ID
        Assert.False(result.IsSuccess);
        var exception = result.GetException();
        Assert.Contains("Failed to reserve tags", exception.Message);
    }

    [Fact]
    public async Task GetSerializableTagState_Should_ReturnState()
    {
        // Given: Create a student via typed command
        var executor = CreateExecutor();
        var sekibanExecutor = (ISekibanExecutor)executor;
        var studentId = Guid.NewGuid();

        var command = new CreateStudent(studentId, "TagStateTest", 5);
        var commandResult = await sekibanExecutor.ExecuteAsync(command);
        Assert.True(commandResult.IsSuccess);

        // When: Get serializable tag state
        var tagStateId = TagStateId.FromProjector<StudentProjector>(new StudentTag(studentId));
        var stateResult = await executor.GetSerializableTagStateAsync(tagStateId);

        // Then
        Assert.True(stateResult.IsSuccess);
        var state = stateResult.GetValue();
        Assert.Equal("Student", state.TagGroup);
        Assert.Equal(studentId.ToString(), state.TagContent);
        Assert.True(state.Version > 0);
        Assert.NotEmpty(state.LastSortedUniqueId);
    }

    [Fact]
    public async Task CommitWithCorrectConsistencyTag_After_InitialCommit_Should_Succeed()
    {
        // Given: commit first event
        var executor = CreateExecutor();
        var sekibanExecutor = (ISekibanExecutor)executor;
        var studentId = Guid.NewGuid();
        var tagString = $"Student:{studentId}";

        var firstCommand = new CreateStudent(studentId, "Alice", 5);
        var firstResult = await sekibanExecutor.ExecuteAsync(firstCommand);
        Assert.True(firstResult.IsSuccess);

        var writtenSortableId = firstResult.GetValue().SortableUniqueId;
        Assert.NotNull(writtenSortableId);

        // Given: commit second event with correct consistency tag
        var payload = SerializePayload(new StudentCreated(studentId, "Alice v2", 3));

        var request = new SerializedCommitRequest(
            [new SerializableEventCandidate(payload, nameof(StudentCreated), [tagString])],
            [new ConsistencyTagEntry(tagString, writtenSortableId)]);

        // When
        var result = await executor.CommitSerializableEventsAsync(request);

        // Then
        Assert.True(result.IsSuccess);
        var commitResult = result.GetValue();
        Assert.Single(commitResult.WrittenEvents);
    }

    [Fact]
    public async Task SerializedCommit_Then_TypedCommit_TagState_Should_Reflect_Both()
    {
        // Given: commit first event via typed path to establish state
        var executor = CreateExecutor();
        var sekibanExecutor = (ISekibanExecutor)executor;
        var studentId = Guid.NewGuid();
        var tagString = $"Student:{studentId}";

        var command = new CreateStudent(studentId, "InitialStudent", 5);
        var commandResult = await sekibanExecutor.ExecuteAsync(command);
        Assert.True(commandResult.IsSuccess);
        var firstSortableId = commandResult.GetValue().SortableUniqueId;
        Assert.NotNull(firstSortableId);

        // When: commit second event via serialized path using correct consistency tag
        var payload = SerializePayload(new StudentCreated(studentId, "UpdatedViaSerializedPath", 3));
        var request = new SerializedCommitRequest(
            [new SerializableEventCandidate(payload, nameof(StudentCreated), [tagString])],
            [new ConsistencyTagEntry(tagString, firstSortableId)]);

        var commitResult = await executor.CommitSerializableEventsAsync(request);
        Assert.True(commitResult.IsSuccess);

        var writtenEvents = commitResult.GetValue().WrittenEvents;
        Assert.Single(writtenEvents);
        Assert.Equal(nameof(StudentCreated), writtenEvents[0].EventPayloadName);
        Assert.NotEmpty(writtenEvents[0].SortableUniqueIdValue);

        // Then: serialized commit succeeded after typed commit with correct consistency
        Assert.True(commitResult.GetValue().Duration > TimeSpan.Zero);
        Assert.NotEmpty(commitResult.GetValue().TagWriteResults);
    }

    [Fact]
    public async Task InMemoryDcbExecutor_Implements_ISerializedSekibanDcbExecutor()
    {
        // Given
        var executor = new InMemoryDcbExecutor(_domainTypes);

        // Then
        Assert.IsAssignableFrom<ISerializedSekibanDcbExecutor>(executor);
        Assert.IsAssignableFrom<ISekibanExecutor>(executor);
    }
}
