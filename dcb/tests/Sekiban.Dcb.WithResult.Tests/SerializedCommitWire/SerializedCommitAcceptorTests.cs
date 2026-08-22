using System.Text;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     SEK-G17 two-phase acceptance: the version discriminator is read from the raw bytes BEFORE any typed payload binding
///     or side effect. Unknown versions fail closed with a typed <see cref="UnsupportedSerializedCommitEnvelopeVersionException" />
///     and structurally invalid envelopes with a DISTINCT typed <see cref="MalformedSerializedCommitException" />; neither
///     ever reaches the executor. A supported version (and a missing version = legacy) routes to the executor unchanged.
/// </summary>
public class SerializedCommitAcceptorTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>Records whether the executor was invoked, so ordering ("before executor/store") is provable.</summary>
    private sealed class RecordingExecutor : ISerializedSekibanDcbExecutor
    {
        public int CommitCalls { get; private set; }
        public SerializedCommitRequest? LastRequest { get; private set; }

        public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
            throw new NotSupportedException();

        public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
            SerializedCommitRequest request, CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            LastRequest = request;
            return Task.FromResult(
                ResultBox.FromValue(
                    new SerializedCommitResult(
                        Array.Empty<Sekiban.Dcb.Events.SerializableEvent>(),
                        Array.Empty<TagWriteResult>(),
                        TimeSpan.Zero)));
        }
    }

    [Fact]
    public async Task LegacyUnversioned_IsAccepted_AndRoutedToExecutor()
    {
        var exec = new RecordingExecutor();
        var acceptor = new SerializedCommitAcceptor(exec);
        var json = """{"eventCandidates":[{"payload":"AQID","eventPayloadName":"E","tags":["G:1"]}],"consistencyTags":[]}""";

        var result = await acceptor.AcceptAsync(Utf8(json));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, exec.CommitCalls);
        Assert.Single(exec.LastRequest!.EventCandidates);
        Assert.Equal("E", exec.LastRequest!.EventCandidates[0].EventPayloadName);
    }

    [Fact]
    public async Task KnownVersion_IsAccepted_AndRoutedToExecutor()
    {
        var exec = new RecordingExecutor();
        var acceptor = new SerializedCommitAcceptor(exec);
        var json = """{"version":1,"eventCandidates":[{"payload":"AQID","eventPayloadName":"E","tags":["G:1"]}],"consistencyTags":[]}""";

        var result = await acceptor.AcceptAsync(Utf8(json));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, exec.CommitCalls);
    }

    [Fact]
    public async Task UnknownVersion_FailsClosed_WithTypedUnsupportedVersion_ExecutorNeverCalled()
    {
        var exec = new RecordingExecutor();
        var acceptor = new SerializedCommitAcceptor(exec);
        var json = """{"version":999,"eventCandidates":[{"payload":"AQID","eventPayloadName":"E","tags":["G:1"]}],"consistencyTags":[]}""";

        var result = await acceptor.AcceptAsync(Utf8(json));

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<UnsupportedSerializedCommitEnvelopeVersionException>(result.GetException());
        Assert.Equal(999, ex.RequestedVersion);
        Assert.Equal(VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion, ex.SupportedVersion);
        Assert.Equal(0, exec.CommitCalls); // before executor/store
    }

    [Fact]
    public async Task UnknownVersion_IsDecidedBeforePayloadBinding_EvenWithInvalidBase64Payload()
    {
        // Ordering proof: the payload is not valid base64, yet the result is UnsupportedVersion (NOT a shape error),
        // because the version is read before any typed payload deserialization / base64 decode.
        var exec = new RecordingExecutor();
        var acceptor = new SerializedCommitAcceptor(exec);
        var json = """{"version":999,"eventCandidates":[{"payload":"!!!not-base64!!!","eventPayloadName":"E","tags":[]}],"consistencyTags":[]}""";

        var result = await acceptor.AcceptAsync(Utf8(json));

        Assert.IsType<UnsupportedSerializedCommitEnvelopeVersionException>(result.GetException());
        Assert.Equal(0, exec.CommitCalls);
    }

    [Fact]
    public async Task WrongTypedVersion_IsShapeError_DistinctFromUnsupportedVersion()
    {
        var exec = new RecordingExecutor();
        var acceptor = new SerializedCommitAcceptor(exec);
        var json = """{"version":"1","eventCandidates":[],"consistencyTags":[]}"""; // version as a string

        var result = await acceptor.AcceptAsync(Utf8(json));

        Assert.IsType<MalformedSerializedCommitException>(result.GetException());
        Assert.IsNotType<UnsupportedSerializedCommitEnvelopeVersionException>(result.GetException());
        Assert.Equal(0, exec.CommitCalls);
    }

    [Fact]
    public async Task DuplicateExactVersion_IsShapeError()
    {
        var exec = new RecordingExecutor();
        var acceptor = new SerializedCommitAcceptor(exec);
        var json = """{"version":1,"version":1,"eventCandidates":[],"consistencyTags":[]}""";

        var result = await acceptor.AcceptAsync(Utf8(json));

        var ex = Assert.IsType<MalformedSerializedCommitException>(result.GetException());
        Assert.Equal(SerializedCommitShapeError.DuplicateVersion, ex.Reason);
        Assert.Equal(0, exec.CommitCalls);
    }

    [Fact]
    public async Task MixedCaseVersionOnly_IsShapeError_NeverSilentlyLegacyOrV1()
    {
        var exec = new RecordingExecutor();
        var acceptor = new SerializedCommitAcceptor(exec);
        var json = """{"Version":1,"eventCandidates":[],"consistencyTags":[]}"""; // capital V

        var result = await acceptor.AcceptAsync(Utf8(json));

        var ex = Assert.IsType<MalformedSerializedCommitException>(result.GetException());
        Assert.Equal(SerializedCommitShapeError.AmbiguousVersionCasing, ex.Reason);
        Assert.Equal(0, exec.CommitCalls); // NOT routed as legacy or V1
    }

    [Fact]
    public async Task ExactPlusMixedCaseVersion_IsShapeError()
    {
        var exec = new RecordingExecutor();
        var acceptor = new SerializedCommitAcceptor(exec);
        var json = """{"version":1,"Version":1,"eventCandidates":[],"consistencyTags":[]}""";

        var result = await acceptor.AcceptAsync(Utf8(json));

        var ex = Assert.IsType<MalformedSerializedCommitException>(result.GetException());
        Assert.Equal(SerializedCommitShapeError.AmbiguousVersionCasing, ex.Reason);
        Assert.Equal(0, exec.CommitCalls);
    }

    [Fact]
    public async Task KnownVersionButMalformedPayload_IsShapeError_NotUnsupportedVersion()
    {
        var exec = new RecordingExecutor();
        var acceptor = new SerializedCommitAcceptor(exec);
        var json = """{"version":1,"eventCandidates":[{"payload":"!!!not-base64!!!","eventPayloadName":"E","tags":[]}],"consistencyTags":[]}""";

        var result = await acceptor.AcceptAsync(Utf8(json));

        Assert.IsType<MalformedSerializedCommitException>(result.GetException());
        Assert.Equal(0, exec.CommitCalls); // binding failed before execution
    }

    [Theory]
    [InlineData(
        SerializedCommitShapeError.LegacyPayloadInvalid,
        "{\"eventCandidates\":[{\"payload\":\"AQID\",\"eventPayloadName\":\"E\",\"tags\":[\"G:1\"]}],\"consistencyTags\":[{\"tag\":\"G:1\",\"lastSortableUniqueId\":null}]}")]
    [InlineData(
        SerializedCommitShapeError.VersionedPayloadInvalid,
        "{\"version\":1,\"eventCandidates\":[{\"payload\":\"AQID\",\"eventPayloadName\":\"E\",\"tags\":[\"G:1\"]}],\"consistencyTags\":[{\"tag\":\"G:1\",\"lastSortableUniqueId\":null}]}")]
    public async Task NullReservationVersion_IsTypedShapeError_BeforeExecutorIo(
        SerializedCommitShapeError expected,
        string json)
    {
        var exec = new RecordingExecutor();
        var acceptor = new SerializedCommitAcceptor(exec);

        var result = await acceptor.AcceptAsync(Utf8(json));

        var error = Assert.IsType<MalformedSerializedCommitException>(result.GetException());
        Assert.Equal(expected, error.Reason);
        Assert.Equal(0, exec.CommitCalls);
    }

    [Fact]
    public async Task NonObjectRoot_IsShapeError_NeverNullReference()
    {
        var exec = new RecordingExecutor();
        var acceptor = new SerializedCommitAcceptor(exec);

        foreach (var json in new[] { "[]", "\"hello\"", "123", "null" })
        {
            var result = await acceptor.AcceptAsync(Utf8(json));
            Assert.IsType<MalformedSerializedCommitException>(result.GetException());
            Assert.Equal(0, exec.CommitCalls);
        }
    }

    [Theory]
    [InlineData(SerializedCommitVersionKind.KnownVersion, null, """{"version":1,"eventCandidates":[]}""")]           // V1 known
    [InlineData(SerializedCommitVersionKind.KnownVersion, null, """{"version":2,"eventCandidates":[]}""")]           // V2 known (shape binds later)
    [InlineData(SerializedCommitVersionKind.UnsupportedVersion, null, """{"version":999,"eventCandidates":[]}""")]   // exact unknown
    [InlineData(SerializedCommitVersionKind.LegacyUnversioned, null, """{"eventCandidates":[],"consistencyTags":[]}""")] // missing → legacy
    [InlineData(SerializedCommitVersionKind.Malformed, SerializedCommitShapeError.VersionNotInteger, """{"version":"1"}""")]   // wrong type (string)
    [InlineData(SerializedCommitVersionKind.Malformed, SerializedCommitShapeError.VersionNotInteger, """{"version":1.5}""")]   // wrong type (float)
    [InlineData(SerializedCommitVersionKind.Malformed, SerializedCommitShapeError.VersionNotInteger, """{"version":true}""")]  // wrong type (bool)
    [InlineData(SerializedCommitVersionKind.Malformed, SerializedCommitShapeError.DuplicateVersion, """{"version":1,"version":1}""")] // exact duplicate
    [InlineData(SerializedCommitVersionKind.Malformed, SerializedCommitShapeError.AmbiguousVersionCasing, """{"Version":1}""")]       // mixed-case only
    [InlineData(SerializedCommitVersionKind.Malformed, SerializedCommitShapeError.AmbiguousVersionCasing, """{"VERSION":1}""")]       // upper-case only
    [InlineData(SerializedCommitVersionKind.Malformed, SerializedCommitShapeError.AmbiguousVersionCasing, """{"vErSiOn":1}""")]       // arbitrary mixed
    [InlineData(SerializedCommitVersionKind.Malformed, SerializedCommitShapeError.AmbiguousVersionCasing, """{"version":1,"Version":1}""")] // exact + mixed duplicate
    [InlineData(SerializedCommitVersionKind.Malformed, SerializedCommitShapeError.NonObjectRoot, "[]")]              // non-object root
    [InlineData(SerializedCommitVersionKind.Malformed, SerializedCommitShapeError.UnreadableJson, "not json at all")] // unreadable
    public void Discriminator_ClassifiesRawVersion(
        SerializedCommitVersionKind expectedKind, SerializedCommitShapeError? expectedError, string json)
    {
        var result = SerializedCommitVersionDiscriminator.Read(Utf8(json));
        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedError, result.ShapeError);
    }

    [Fact]
    public void ContractOptions_AreCaseSensitive_NeverAmbientCaseInsensitive()
    {
        // A PascalCase 'Version' must NOT bind to the camelCase contract property (ambient web-defaults case-insensitivity
        // is deliberately not inherited by the contract-owned options).
        Assert.False(SerializedCommitWireContract.Options.PropertyNameCaseInsensitive);
    }

    [Fact]
    public async Task V2ExpectedTagPositions_RoutesOnlyToTheAdditiveExecutorCapability()
    {
        var exec = new ExpectedRecordingExecutor();
        var json = """
                   {"version":2,"eventCandidates":[{"payload":"AQID","eventPayloadName":"E","tags":["G:1"]}],"consistencyTags":[{"tag":"G:1","lastSortableUniqueId":""}],"expectedTagPositions":[{"serviceId":"default","tag":"G:1","expectation":{"kind":3,"position":"p-1"}}]}
                   """;

        var result = await new SerializedCommitAcceptor(exec).AcceptAsync(Utf8(json));

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(0, exec.LegacyCalls);
        var request = Assert.IsType<VersionedExpectedTagPositionSerializedCommitRequest>(exec.Request);
        Assert.Equal(VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion, request.Version);
        var entry = Assert.Single(request.ExpectedTagPositions);
        Assert.Equal(TagHeadExpectationKind.Exact, entry.Expectation.Kind);
        Assert.Equal("p-1", entry.Expectation.Position);
    }

    [Fact]
    public async Task V2ExpectedTagPositions_UnsupportedExecutorFailsBeforeLegacyExecutorInvocation()
    {
        var exec = new RecordingExecutor();
        var json = """
                   {"version":2,"eventCandidates":[],"consistencyTags":[],"expectedTagPositions":[]}
                   """;

        var result = await new SerializedCommitAcceptor(exec).AcceptAsync(Utf8(json));

        Assert.False(result.IsSuccess);
        Assert.IsType<ConditionNotSupportedException>(result.GetException());
        Assert.Equal(0, exec.CommitCalls);
    }

    private sealed class ExpectedRecordingExecutor : ISerializedSekibanDcbExecutor,
        ISerializedExpectedTagPositionSekibanDcbExecutor
    {
        public int LegacyCalls { get; private set; }
        public VersionedExpectedTagPositionSerializedCommitRequest? Request { get; private set; }

        public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
            throw new NotSupportedException();

        public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
            SerializedCommitRequest request, CancellationToken cancellationToken = default)
        {
            LegacyCalls++;
            throw new InvalidOperationException("V2 must not fall through to the legacy serialized executor.");
        }

        public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsWithExpectedTagPositionsAsync(
            VersionedExpectedTagPositionSerializedCommitRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(ResultBox.FromValue(new SerializedCommitResult(
                Array.Empty<Sekiban.Dcb.Events.SerializableEvent>(),
                Array.Empty<TagWriteResult>(),
                TimeSpan.Zero)));
        }
    }
}
