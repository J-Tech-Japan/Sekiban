using System.Reflection;
using System.Text;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     SEK-G17 no-migration guarantees, proven structurally:
///     <list type="bullet">
///         <item>the existing positional DTOs carry NO serialization attributes (pinning is contract-owned, so ambient
///         serialization is unchanged);</item>
///         <item>the stored-event schema and the serialized write/read entry points are signature-frozen;</item>
///         <item>every 10.1.17-valid shape (including the empty request) is still accepted — no narrowing.</item>
///     </list>
/// </summary>
public class SerializedCommitNoMigrationTests
{
    private static bool IsJsonAttribute(object attr) =>
        attr.GetType().Namespace == "System.Text.Json.Serialization";

    private static void AssertNoSerializationAttributes(Type type)
    {
        Assert.DoesNotContain(type.GetCustomAttributes(false), IsJsonAttribute);
        foreach (var p in type.GetProperties())
        {
            Assert.DoesNotContain(p.GetCustomAttributes(false), IsJsonAttribute);
        }
        foreach (var ctor in type.GetConstructors())
        {
            foreach (var param in ctor.GetParameters())
            {
                Assert.DoesNotContain(param.GetCustomAttributes(false), IsJsonAttribute);
            }
        }
    }

    [Fact]
    public void PositionalWireDtos_CarryNoSerializationAttributes()
    {
        // If any attribute (even a baseline-neutral [JsonPropertyName]) were added to these, a fresh-options consumer's
        // PascalCase output would change. It must not.
        AssertNoSerializationAttributes(typeof(SerializedCommitRequest));
        AssertNoSerializationAttributes(typeof(SerializableEventCandidate));
        AssertNoSerializationAttributes(typeof(ConsistencyTagEntry));
        AssertNoSerializationAttributes(typeof(SerializedCommitResult));
    }

    [Fact]
    public void StoredEventSchema_IsFrozen()
    {
        // The stored event shape (what the write path persists) is unchanged by this slice.
        var ctor = Assert.Single(typeof(SerializableEvent).GetConstructors());
        var shape = string.Join(", ", ctor.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Assert.Equal(
            "Byte[] Payload, String SortableUniqueIdValue, Guid Id, EventMetadata EventMetadata, List`1 Tags, String EventPayloadName",
            shape);
    }

    [Fact]
    public void SerializedExecutorWritePath_SignaturesAreFrozen()
    {
        var signatures = typeof(ISerializedSekibanDcbExecutor).GetMethods()
            .Select(m => $"{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "Task`1 CommitSerializableEventsAsync(SerializedCommitRequest, CancellationToken)",
                "Task`1 GetSerializableTagStateAsync(TagStateId)"
            },
            signatures);
    }

    private sealed class AcceptingExecutor : ISerializedSekibanDcbExecutor
    {
        public int Calls { get; private set; }
        public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
            throw new NotSupportedException();
        public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
            SerializedCommitRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(
                ResultBox.FromValue(
                    new SerializedCommitResult(
                        Array.Empty<SerializableEvent>(), Array.Empty<TagWriteResult>(), TimeSpan.Zero)));
        }
    }

    // Every one of these is a valid 10.1.17 official-shape request; none may be narrowed (rejected) by the new acceptor.
    public static IEnumerable<object[]> LegacyValidShapes() => new[]
    {
        new object[] { """{"eventCandidates":[],"consistencyTags":[]}""" },                                   // empty request
        new object[] { """{"eventCandidates":[{"payload":"AQ==","eventPayloadName":"E","tags":[]}],"consistencyTags":[]}""" }, // event, no tags
        new object[] { """{"eventCandidates":[{"payload":"AQ==","eventPayloadName":"E","tags":["G:a"]}],"consistencyTags":[{"tag":"G:a","lastSortableUniqueId":""}]}""" },
        new object[] { """{"eventCandidates":[{"payload":"AQ==","eventPayloadName":"E","tags":["G:a","H:b"]},{"payload":"Ag==","eventPayloadName":"F","tags":["H:b"]}],"consistencyTags":[]}""" }
    };

    [Theory]
    [MemberData(nameof(LegacyValidShapes))]
    public async Task LegacyValidShapes_AreNotNarrowed_Unversioned(string json)
    {
        var exec = new AcceptingExecutor();
        var result = await new SerializedCommitAcceptor(exec).AcceptAsync(Encoding.UTF8.GetBytes(json));
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(1, exec.Calls); // routed to the executor, not rejected
    }

    [Theory]
    [MemberData(nameof(LegacyValidShapes))]
    public async Task LegacyValidShapes_AlsoAccepted_WhenExplicitlyV1(string json)
    {
        // Prefix an explicit "version":1 — the same shapes remain acceptable through the versioned path.
        var versioned = "{\"version\":1," + json.Substring(1);
        var exec = new AcceptingExecutor();
        var result = await new SerializedCommitAcceptor(exec).AcceptAsync(Encoding.UTF8.GetBytes(versioned));
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(1, exec.Calls);
    }
}
