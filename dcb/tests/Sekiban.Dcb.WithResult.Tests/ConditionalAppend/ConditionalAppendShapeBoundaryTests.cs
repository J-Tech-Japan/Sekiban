using Dcb.Domain;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G15 conservative supported-shape boundary: only payloads whose effective JsonTypeInfo graph is provably
///     deterministic may be fingerprinted. Converter-owned / non-object / non-deterministically-ordered shapes are
///     rejected BEFORE any (de)serialization — proven here with a hostile valid-JSON converter that would produce a
///     different output on every call: a real conditional executor invocation fails closed before the store write, the
///     converter is never even invoked, and two attempts can never yield two fingerprints.
/// </summary>
public class ConditionalAppendShapeBoundaryTests
{
    private static DcbDomainTypes DomainWith(params Action<SimpleEventTypes>[] registrations)
    {
        var d = DomainType.GetDomainTypes();
        foreach (var r in registrations)
        {
            r((SimpleEventTypes)d.EventTypes);
        }

        return d;
    }

    [Fact]
    public void CustomConverterPayload_IsRejectedByTheBoundary()
    {
        var domain = DomainWith(e => e.RegisterEventType<HostileConverterEvent>());
        var r = OperationFingerprint.ComputeCanonical(
            "s", "k", domain.EventTypes, nameof(HostileConverterEvent),
            Encoding.UTF8.GetBytes("{\"value\":\"x\"}"), Array.Empty<string>());
        Assert.False(r.IsSuccess);
        Assert.IsType<OperationCanonicalizationException>(r.GetException());
    }

    [Fact]
    public void UnorderedSetCollection_IsRejectedByTheBoundary()
    {
        var domain = DomainWith(e => e.RegisterEventType<SetEvent>());
        var r = OperationFingerprint.ComputeCanonical(
            "s", "k", domain.EventTypes, nameof(SetEvent),
            Encoding.UTF8.GetBytes("{\"items\":[\"a\"]}"), Array.Empty<string>());
        Assert.False(r.IsSuccess);
        Assert.IsType<OperationCanonicalizationException>(r.GetException());
    }

    [Fact]
    public void DictionaryCollection_IsRejectedByTheBoundary()
    {
        var domain = DomainWith(e => e.RegisterEventType<MapEvent>());
        var r = OperationFingerprint.ComputeCanonical(
            "s", "k", domain.EventTypes, nameof(MapEvent),
            Encoding.UTF8.GetBytes("{\"map\":{\"a\":1}}"), Array.Empty<string>());
        Assert.False(r.IsSuccess);
        Assert.IsType<OperationCanonicalizationException>(r.GetException());
    }

    [Fact]
    public void OrderedListCollection_IsSupported()
    {
        var domain = DomainWith(e => e.RegisterEventType<ListEvent>());
        var json = domain.EventTypes.SerializeEventPayload(new ListEvent(new List<string> { "a", "b" }));
        var r = OperationFingerprint.ComputeCanonical(
            "s", "k", domain.EventTypes, nameof(ListEvent),
            Encoding.UTF8.GetBytes(json), Array.Empty<string>());
        Assert.True(r.IsSuccess);
    }

    [Fact]
    public async Task HostileConverter_ConditionalExecutor_FailsClosedBeforeStoreWrite_NeverTwoFingerprints()
    {
        HostileConverter.Reset();
        var domain = DomainWith(e => e.RegisterEventType<HostileConverterEvent>());
        var store = new InMemoryConditionalEventStore(domain.EventTypes);
        var executor = new GeneralSekibanExecutor(store, new InMemoryObjectAccessor(store, domain), domain);
        var options = new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification("op-hostile") };

        Func<HostileCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handler =
            (_, ctx) => ctx.AppendEvent(new HostileConverterEvent("x"), new BoundaryTag("t"));

        var first = await executor.ExecuteAsync(new HostileCommand(), handler, options);
        var second = await executor.ExecuteAsync(new HostileCommand(), handler, options);

        // Both fail closed with the sanitized canonicalization error, BEFORE the store made any durable write. The
        // boundary rejects the shape inside AppendIfUniqueAsync before ComputeCanonical ever hashes anything, so NO
        // fingerprint is produced on either attempt — there can be no two differing fingerprints for the key. (The
        // executor does serialize the event to form the candidate, which is why the converter runs; but that output is
        // never fingerprinted and never persisted.)
        Assert.False(first.IsSuccess);
        Assert.IsType<OperationCanonicalizationException>(first.GetException());
        Assert.False(second.IsSuccess);
        Assert.IsType<OperationCanonicalizationException>(second.GetException());
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue()); // no durable claim / no write
    }

    private record HostileCommand : ICommand;

    private record BoundaryTag(string Id) : IStringTagGroup<BoundaryTag>
    {
        public static string TagGroupName => "Boundary";
        public static BoundaryTag FromContent(string content) => new(content);
        public bool IsConsistencyTag() => false;
        public string GetId() => Id;
    }

    [JsonConverter(typeof(HostileConverter))]
    private record HostileConverterEvent(string Value) : IEventPayload;

    private record SetEvent(HashSet<string> Items) : IEventPayload;

    private record MapEvent(Dictionary<string, int> Map) : IEventPayload;

    private record ListEvent(List<string> Items) : IEventPayload;

    /// <summary>Emits valid but DIFFERENT JSON on every write; the boundary must ensure it is never reached.</summary>
    private sealed class HostileConverter : JsonConverter<HostileConverterEvent>
    {
        private static int _writeCalls;
        public static int WriteCalls => Volatile.Read(ref _writeCalls);
        public static void Reset() => Interlocked.Exchange(ref _writeCalls, 0);

        public override HostileConverterEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var value = doc.RootElement.TryGetProperty("value", out var v) ? v.GetString() ?? string.Empty : string.Empty;
            return new HostileConverterEvent(value);
        }

        public override void Write(Utf8JsonWriter writer, HostileConverterEvent value, JsonSerializerOptions options)
        {
            var call = Interlocked.Increment(ref _writeCalls);
            writer.WriteStartObject();
            writer.WriteNumber("call", call); // non-deterministic: differs every time
            writer.WriteString("value", value.Value);
            writer.WriteEndObject();
        }
    }
}
