using System.Text.Json;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.LegacyConsumerFixture;

/// <summary>
///     A SEPARATE producer written the way a dcb-v10.1.17 program did, compiled against the CURRENT Sekiban assemblies. It
///     uses ONLY the pre-SEK-G17 public surfaces — the positional <see cref="SerializedCommitRequest" /> with the
///     heterogeneous per-event tags model (<see cref="SerializableEventCandidate.Tags" />) and
///     <see cref="ConsistencyTagEntry" /> — and serializes to the unversioned official wire shape with plain
///     System.Text.Json web defaults, exactly as a 10.1.17 producer did. It never references the SEK-G17 envelope,
///     acceptor, adapter, or contract serializer, and it does not reference any domain assembly (event payloads are built
///     as anonymous objects, the way an external producer emits JSON without the .NET event types).
///     <para>
///         The behavioral no-migration test in the main test assembly feeds THESE bytes (the old producer's artifact,
///         crossing the assembly boundary) into the new acceptance surface + a real executor. That this producer still
///         COMPILES and that its bytes still COMMIT unchanged is the 10.1.x no-migration proof.
///     </para>
/// </summary>
public static class Legacy1017WireConsumer
{
    // Event payload names must match registered domain event types on the consuming side; these are the Student domain's.
    public const string Student1Id = "11111111-1111-1111-1111-111111111111";
    public const string Student2Id = "22222222-2222-2222-2222-222222222222";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The exact payload #1 bytes this producer embeds (a StudentCreated-shaped JSON object).</summary>
    public static byte[] Payload1() =>
        JsonSerializer.SerializeToUtf8Bytes(new { studentId = Student1Id, name = "Alice", maxClassCount = 5 }, Web);

    /// <summary>The exact payload #2 bytes this producer embeds.</summary>
    public static byte[] Payload2() =>
        JsonSerializer.SerializeToUtf8Bytes(new { studentId = Student2Id, name = "Bob", maxClassCount = 3 }, Web);

    /// <summary>Event #1 carries two tags, event #2 one tag — heterogeneous per-event tags, not flattened.</summary>
    public static IReadOnlyList<string> Event1Tags => new[] { $"Student:{Student1Id}", $"Student:{Student2Id}" };
    public static IReadOnlyList<string> Event2Tags => new[] { $"Student:{Student2Id}" };

    /// <summary>Builds the 10.1.17 positional request with heterogeneous per-event tags using only pre-G17 surfaces.</summary>
    public static SerializedCommitRequest BuildRequestWithPerEventTags() =>
        new(
            new List<SerializableEventCandidate>
            {
                new(Payload1(), "StudentCreated", Event1Tags.ToList()),
                new(Payload2(), "StudentCreated", Event2Tags.ToList())
            },
            new List<ConsistencyTagEntry>());

    /// <summary>Serializes to the unversioned official wire shape the way a 10.1.17 producer did (web defaults, camelCase).</summary>
    public static byte[] SerializeUnversionedWire() =>
        JsonSerializer.SerializeToUtf8Bytes(BuildRequestWithPerEventTags(), Web);

    /// <summary>The 10.1.17 empty-request wire bytes (valid empty commit) — used to prove empty-request compatibility.</summary>
    public static byte[] SerializeEmptyUnversionedWire() =>
        JsonSerializer.SerializeToUtf8Bytes(
            new SerializedCommitRequest(Array.Empty<SerializableEventCandidate>(), Array.Empty<ConsistencyTagEntry>()), Web);
}
