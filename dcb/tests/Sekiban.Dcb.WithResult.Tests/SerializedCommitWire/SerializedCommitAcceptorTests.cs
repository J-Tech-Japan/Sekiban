using System.Text;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
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
        Assert.Equal(VersionedSerializedCommitRequest.CurrentVersion, ex.SupportedVersion);
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
    public async Task DuplicateVersion_IsShapeError()
    {
        var exec = new RecordingExecutor();
        var acceptor = new SerializedCommitAcceptor(exec);
        var json = """{"version":1,"version":1,"eventCandidates":[],"consistencyTags":[]}""";

        var result = await acceptor.AcceptAsync(Utf8(json));

        var ex = Assert.IsType<MalformedSerializedCommitException>(result.GetException());
        Assert.Contains("duplicate", ex.Detail, StringComparison.OrdinalIgnoreCase);
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
    [InlineData(SerializedCommitVersionKind.LegacyUnversioned, """{"eventCandidates":[],"consistencyTags":[]}""")]
    [InlineData(SerializedCommitVersionKind.KnownVersion, """{"version":1,"eventCandidates":[]}""")]
    [InlineData(SerializedCommitVersionKind.UnsupportedVersion, """{"version":2,"eventCandidates":[]}""")]
    [InlineData(SerializedCommitVersionKind.Malformed, """{"version":1.5}""")]
    [InlineData(SerializedCommitVersionKind.Malformed, """{"version":true}""")]
    [InlineData(SerializedCommitVersionKind.Malformed, "not json at all")]
    public void Discriminator_ClassifiesRawVersion(SerializedCommitVersionKind expected, string json)
    {
        Assert.Equal(expected, SerializedCommitVersionDiscriminator.Read(Utf8(json)).Kind);
    }
}
