using System.Text;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     SEK-G17 secret-safety: a hostile serialized-commit request must not be able to smuggle its content out through the
///     typed error surface. Every malformed input below embeds a unique SENTINEL string in the place a naive implementation
///     would echo (JSON text, property key, `version` value, base64 payload, event type name, tag). The public acceptor's
///     error is inspected RECURSIVELY (Message, Data keys/values, InnerException chain, StackTrace, ToString) and must
///     contain NO sentinel. The executor is also proven never called — no downstream work on a malformed request.
/// </summary>
public class SerializedCommitMalformedSecretSafetyTests
{
    private const string Sentinel = "S3NT1NEL_SECRET_d0not_leak";

    private sealed class NeverCalledExecutor : ISerializedSekibanDcbExecutor
    {
        public int Calls { get; private set; }
        public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
            throw new NotSupportedException();
        public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
            SerializedCommitRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("executor must not be called for a malformed request");
        }
    }

    public static IEnumerable<object[]> HostileInputs() => new[]
    {
        // Non-object root whose text is the secret.
        new object[] { "\"" + Sentinel + "\"" },
        // Not well-formed JSON, secret in a dangling key.
        new object[] { "{\"" + Sentinel + "\": " },
        // Wrong-typed version carrying the secret.
        new object[] { "{\"version\":\"" + Sentinel + "\"}" },
        // Secret property key alongside a bad version.
        new object[] { "{\"" + Sentinel + "\":1,\"version\":1.5}" },
        // Aliased top-level collection names must also be fixed-message failures, even when a value holds the secret.
        new object[] { "{\"candidates\":[\"" + Sentinel + "\"],\"consistency\":[]}" },
        // Known version but invalid base64 payload + secret type name / tag.
        new object[]
        {
            "{\"version\":1,\"eventCandidates\":[{\"payload\":\"" + Sentinel + "!!not-base64\",\"eventPayloadName\":\""
            + Sentinel + "\",\"tags\":[\"" + Sentinel + ":x\"]}],\"consistencyTags\":[]}"
        },
        // Legacy (unversioned) but invalid base64 payload + secret.
        new object[]
        {
            "{\"eventCandidates\":[{\"payload\":\"" + Sentinel + "!!not-base64\",\"eventPayloadName\":\"" + Sentinel
            + "\",\"tags\":[]}],\"consistencyTags\":[]}"
        }
    };

    [Theory]
    [MemberData(nameof(HostileInputs))]
    public async Task MalformedRequest_LeaksNoSecret_AndNeverCallsExecutor(string hostileJson)
    {
        var exec = new NeverCalledExecutor();
        var acceptor = new SerializedCommitAcceptor(exec);

        var result = await acceptor.AcceptAsync(Encoding.UTF8.GetBytes(hostileJson));

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<MalformedSerializedCommitException>(result.GetException());

        foreach (var text in AllStrings(ex))
        {
            Assert.DoesNotContain(Sentinel, text, StringComparison.Ordinal);
        }
        Assert.Null(ex.InnerException);          // no arbitrary inner cause attached
        Assert.Equal(0, exec.Calls);             // no downstream work
    }

    private static IEnumerable<string> AllStrings(Exception? ex)
    {
        while (ex is not null)
        {
            yield return ex.Message ?? string.Empty;
            yield return ex.ToString() ?? string.Empty; // type + message + stack + inner
            yield return ex.StackTrace ?? string.Empty;
            foreach (System.Collections.DictionaryEntry entry in ex.Data)
            {
                yield return entry.Key?.ToString() ?? string.Empty;
                yield return entry.Value?.ToString() ?? string.Empty;
            }
            ex = ex.InnerException;
        }
    }
}
