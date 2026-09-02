using System.Security.Cryptography;
using System.Text;
using Dcb.Domain;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.TestSupport;
using Xunit;
namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     SEK-G51 raw collection-shape gate. These cases deliberately use the public acceptor rather than a DTO binding so
///     each rejection proves phase 1 stops before the execution, event-store, reservation, and ID-allocation seams.
/// </summary>
public class SerializedCommitCollectionShapeGateTests
{
    private const int FrozenAliasedFixtureLength = 91;
    private const string FrozenAliasedFixtureSha256 = "5a37e6735553f2025a3a005da9ded1a77f1b35d41aed8ac4ca7bcdd6f121e383";

    private static readonly int?[] Versions = [null, VersionedSerializedCommitRequest.CurrentVersion,
        VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion];

    public static IEnumerable<object[]> RejectionMatrix()
    {
        foreach (var version in Versions)
        {
            var v2 = version == VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion
                ? "\"expectedTagPositions\":[]"
                : null;
            var complete = CompleteOfficialMembers(version);

            yield return Row(version, "neither-official-member", SerializedCommitShapeError.MissingOfficialCollectionMembers,
                Envelope(version, v2));
            yield return Row(version, "event-candidates-only", SerializedCommitShapeError.MissingOfficialCollectionMembers,
                Envelope(version, "\"eventCandidates\":[]", v2));
            yield return Row(version, "consistency-tags-only", SerializedCommitShapeError.MissingOfficialCollectionMembers,
                Envelope(version, "\"consistencyTags\":[]", v2));
            yield return Row(version, "both-client-aliases", SerializedCommitShapeError.AliasedCollectionMember,
                Envelope(version, "\"candidates\":[]", "\"consistency\":[]", v2));
            yield return Row(version, "mixed-candidates-alias", SerializedCommitShapeError.AliasedCollectionMember,
                Envelope(version, "\"eventCandidates\":[]", "\"consistencyTags\":[]", "\"candidates\":[]", v2));
            yield return Row(version, "mixed-consistency-alias", SerializedCommitShapeError.AliasedCollectionMember,
                Envelope(version, "\"eventCandidates\":[]", "\"consistencyTags\":[]", "\"consistency\":[]", v2));
            yield return Row(version, "duplicate-event-candidates", SerializedCommitShapeError.DuplicateCollectionMember,
                Envelope(version, "\"eventCandidates\":[]", "\"eventCandidates\":[]", "\"consistencyTags\":[]", v2));
            yield return Row(version, "duplicate-consistency-tags", SerializedCommitShapeError.DuplicateCollectionMember,
                Envelope(version, "\"eventCandidates\":[]", "\"consistencyTags\":[]", "\"consistencyTags\":[]", v2));
            yield return Row(version, "case-variant-event-candidates", SerializedCommitShapeError.AmbiguousCollectionMemberCasing,
                Envelope(version, "\"EventCandidates\":[]", "\"consistencyTags\":[]", v2));
            yield return Row(version, "case-variant-consistency-tags", SerializedCommitShapeError.AmbiguousCollectionMemberCasing,
                Envelope(version, "\"eventCandidates\":[]", "\"ConsistencyTags\":[]", v2));
            yield return Row(version, "duplicate-expected-tag-positions", SerializedCommitShapeError.DuplicateCollectionMember,
                Envelope(version, "\"eventCandidates\":[]", "\"consistencyTags\":[]", "\"expectedTagPositions\":[]", "\"expectedTagPositions\":[]"));
            yield return Row(version, "case-variant-expected-tag-positions", SerializedCommitShapeError.AmbiguousCollectionMemberCasing,
                Envelope(version, "\"eventCandidates\":[]", "\"consistencyTags\":[]", "\"ExpectedTagPositions\":[]"));

            if (version == VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion)
            {
                yield return Row(version, "missing-v2-expected-tag-positions", SerializedCommitShapeError.MissingV2ExpectedTagPositions,
                    Envelope(version, "\"eventCandidates\":[]", "\"consistencyTags\":[]"));
            }
            else
            {
                yield return Row(version, "v2-member-on-non-v2", SerializedCommitShapeError.UnexpectedV2ExpectedTagPositions,
                    Envelope(version, complete, "\"expectedTagPositions\":[]"));
            }
        }
    }

    public static IEnumerable<object[]> CompleteOfficialBodiesWithExtension()
    {
        foreach (var version in Versions)
        {
            yield return new object[]
            {
                VersionName(version),
                Envelope(version, CompleteOfficialMembers(version), "\"x-trace\":\"t1\"")
            };
        }
    }

    [Theory]
    [MemberData(nameof(RejectionMatrix))]
    public async Task RawCollectionShapeMatrix_IsRejectedBeforeEveryExecutionSeam(
        string _,
        SerializedCommitShapeError expectedReason,
        string json)
    {
        var seams = new CountingExecutionSeams();

        var result = await new SerializedCommitAcceptor(seams).AcceptAsync(Encoding.UTF8.GetBytes(json));

        var error = Assert.IsType<MalformedSerializedCommitException>(result.GetException());
        Assert.Equal(expectedReason, error.Reason);
        AssertNoSideEffects(seams);
    }

    [Theory]
    [MemberData(nameof(CompleteOfficialBodiesWithExtension))]
    public async Task CompleteOfficialBodies_WithAnUnrelatedExtension_AreAccepted(
        string _,
        string json)
    {
        var seams = new CountingExecutionSeams();

        var result = await new SerializedCommitAcceptor(seams).AcceptAsync(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(1, seams.ExecutorCalls);
        Assert.Equal(1, seams.EventStoreCalls);
        Assert.Equal(1, seams.ReservationCalls);
        Assert.Equal(1, seams.IdAllocationCalls);
    }

    [Fact]
    public async Task PopulatedMixedAlias_IsRejectedBeforeTheRealStoreReservationAndIdAllocationSeams()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var store = new ProviderWriteCountingEventStore(
            new Sekiban.Dcb.Testing.InMemoryEventStore(domainTypes.EventTypes),
            "in-memory");
        var reservations = new ReservationCountingAccessor();
        var ids = new CountingSortableUniqueIdGenerator();
        var executor = new GeneralSekibanExecutor(
            store,
            reservations,
            domainTypes,
            null,
            null,
            ids,
            new SortableUniqueIdSeedCoordinator(ids),
            new DefaultServiceIdProvider());
        var json = """
                   {"eventCandidates":[{"payload":"AQID","eventPayloadName":"E","tags":["G:1"]}],"consistencyTags":[{"tag":"G:1","lastSortableUniqueId":""}],"candidates":[]}
                   """;

        var result = await new SerializedCommitAcceptor(executor).AcceptAsync(Encoding.UTF8.GetBytes(json));

        var error = Assert.IsType<MalformedSerializedCommitException>(result.GetException());
        Assert.Equal(SerializedCommitShapeError.AliasedCollectionMember, error.Reason);
        Assert.Equal(0, store.ProviderWriteCalls);
        Assert.Equal(0, reservations.ReservationCalls);
        Assert.Equal(0, ids.GenerateCalls);
    }

    [Fact]
    public async Task FrozenTypeScriptClientAliasFixture_FailsClosedInsteadOfCommittingEmpty()
    {
        // Mutation guard: if the raw gate is removed, this reaches the executor and returns a successful empty commit,
        // causing this assertion to fail. The fixture is embedded rather than rebuilt from current DTOs.
        var seams = new CountingExecutionSeams();
        var fixture = LoadFrozen("ts_client_aliased_unversioned.json");
        Assert.Equal(FrozenAliasedFixtureLength, fixture.Length);
        Assert.Equal(FrozenAliasedFixtureSha256, Convert.ToHexString(SHA256.HashData(fixture)).ToLowerInvariant());

        var result = await new SerializedCommitAcceptor(seams).AcceptAsync(fixture);

        Assert.False(result.IsSuccess);
        var error = Assert.IsType<MalformedSerializedCommitException>(result.GetException());
        Assert.Equal(SerializedCommitShapeError.AliasedCollectionMember, error.Reason);
        AssertNoSideEffects(seams);
    }

    private static object[] Row(int? version, string caseName, SerializedCommitShapeError expectedReason, string json) =>
        [VersionName(version) + ":" + caseName, expectedReason, json];

    private static string VersionName(int? version) => version switch
    {
        null => "legacy",
        VersionedSerializedCommitRequest.CurrentVersion => "v1",
        VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion => "v2",
        _ => "unknown"
    };

    private static string CompleteOfficialMembers(int? version) => version == VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion
        ? "\"eventCandidates\":[],\"consistencyTags\":[],\"expectedTagPositions\":[]"
        : "\"eventCandidates\":[],\"consistencyTags\":[]";

    private static string Envelope(int? version, params string?[] members)
    {
        var allMembers = members.Where(member => !string.IsNullOrWhiteSpace(member)).ToList();
        if (version is not null)
        {
            allMembers.Insert(0, "\"version\":" + version.Value);
        }
        return "{" + string.Join(",", allMembers) + "}";
    }

    private static byte[] LoadFrozen(string fileName)
    {
        var assembly = typeof(SerializedCommitCollectionShapeGateTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void AssertNoSideEffects(CountingExecutionSeams seams)
    {
        Assert.Equal(0, seams.ExecutorCalls);
        Assert.Equal(0, seams.EventStoreCalls);
        Assert.Equal(0, seams.ReservationCalls);
        Assert.Equal(0, seams.IdAllocationCalls);
    }

    /// <summary>
    ///     Models all execution-phase seams behind the acceptor boundary. A call reaches all four counters together;
    ///     shape-gate rejections therefore prove none of those downstream categories can begin.
    /// </summary>
    private sealed class CountingExecutionSeams : ISerializedSekibanDcbExecutor,
        ISerializedExpectedTagPositionSekibanDcbExecutor
    {
        public int ExecutorCalls { get; private set; }
        public int EventStoreCalls { get; private set; }
        public int ReservationCalls { get; private set; }
        public int IdAllocationCalls { get; private set; }

        public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
            throw new NotSupportedException();

        public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
            SerializedCommitRequest request,
            CancellationToken cancellationToken = default) => CommitEmptyAsync();

        public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsWithExpectedTagPositionsAsync(
            VersionedExpectedTagPositionSerializedCommitRequest request,
            CancellationToken cancellationToken = default) => CommitEmptyAsync();

        private Task<ResultBox<SerializedCommitResult>> CommitEmptyAsync()
        {
            ExecutorCalls++;
            EventStoreCalls++;
            ReservationCalls++;
            IdAllocationCalls++;
            return Task.FromResult(ResultBox.FromValue(new SerializedCommitResult(
                Array.Empty<Sekiban.Dcb.Events.SerializableEvent>(),
                Array.Empty<TagWriteResult>(),
                TimeSpan.Zero)));
        }
    }

    private sealed class CountingSortableUniqueIdGenerator : ISortableUniqueIdGenerator
    {
        private readonly MonotonicSortableUniqueIdGenerator _inner = new();

        public int GenerateCalls { get; private set; }

        public string GenerateNew()
        {
            GenerateCalls++;
            return _inner.GenerateNew();
        }

        public void Seed(long ticks) => _inner.Seed(ticks);
    }

    private sealed class ReservationCountingAccessor : IActorObjectAccessor
    {
        public int ReservationCalls { get; private set; }

        public Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class
        {
            if (typeof(T) == typeof(ITagConsistentActorCommon))
            {
                return Task.FromResult(ResultBox.FromValue((T)(object)new ReservationCountingActor(this, actorId)));
            }
            return Task.FromResult(ResultBox.Error<T>(new InvalidOperationException("Unexpected actor type.")));
        }

        public Task<bool> ActorExistsAsync(string actorId) => Task.FromResult(false);

        private sealed class ReservationCountingActor(ReservationCountingAccessor owner, string actorId)
            : ITagConsistentActorCommon
        {
            public Task<string> GetTagActorIdAsync() => Task.FromResult(actorId);

            public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
                Task.FromResult(ResultBox.FromValue(string.Empty));

            public Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string? lastSortableUniqueId)
            {
                owner.ReservationCalls++;
                return Task.FromResult(ResultBox.FromValue(
                    new TagWriteReservation("reservation", DateTime.MaxValue.ToString("O"), actorId)));
            }

            public Task<bool> ConfirmReservationAsync(TagWriteReservation reservation) => Task.FromResult(true);

            public Task<bool> CancelReservationAsync(TagWriteReservation reservation) => Task.FromResult(true);

            public Task NotifyEventWrittenAsync() => Task.CompletedTask;
        }
    }
}
