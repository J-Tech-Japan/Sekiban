using Dcb.Domain;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G15 conservative supported-shape boundary. Only a payload whose effective JsonTypeInfo graph is provably built
///     from deterministic, structure-preserving, authoritatively-built-in metadata may be fingerprinted. A custom
///     converter — at the options, type, or property level, even on an OTHERWISE-ALLOWED primitive leaf — is rejected.
///     Ordering note (kept truthful): canonical shape validation happens INSIDE the store's AppendIfUniqueAsync, i.e.
///     AFTER the handler produced the event and after the executor serialized the candidate. So the handler and the
///     payload converter DO run; what the boundary guarantees is that NO fingerprint is computed, NO EventId/append/
///     receipt/durable write happens, and the store's WriteCalls stays zero.
/// </summary>
public class ConditionalAppendShapeBoundaryTests
{
    // ---- ComputeCanonical-level rejection vectors ----

    [Fact]
    public void RootCustomConverter_IsRejected()
    {
        var domain = DomainWith(e => e.RegisterEventType<RootConverterEvent>());
        AssertRejected(domain, nameof(RootConverterEvent), "{\"value\":\"x\"}");
    }

    [Fact]
    public void PropertyLevelConverterOnAllowedLeaf_IsRejected()
    {
        var domain = DomainWith(e => e.RegisterEventType<PropertyConverterEvent>());
        AssertRejected(domain, nameof(PropertyConverterEvent), "{\"name\":\"x\"}");
    }

    [Fact]
    public void TypeLevelConverterOnEnumLeaf_IsRejected()
    {
        var domain = DomainWith(e => e.RegisterEventType<EnumConverterEvent>());
        AssertRejected(domain, nameof(EnumConverterEvent), "{\"color\":\"Red\"}");
    }

    [Fact]
    public void OptionsLevelConverterOnStringLeaf_IsRejected()
    {
        var domain = DomainWithOptionsConverter();
        AssertRejected(domain, nameof(PlainStringEvent), "{\"field\":\"x\"}");
    }

    [Fact]
    public void UnorderedSetCollection_IsRejected()
    {
        var domain = DomainWith(e => e.RegisterEventType<SetEvent>());
        AssertRejected(domain, nameof(SetEvent), "{\"items\":[\"a\"]}");
    }

    [Fact]
    public void DictionaryCollection_IsRejected()
    {
        var domain = DomainWith(e => e.RegisterEventType<MapEvent>());
        AssertRejected(domain, nameof(MapEvent), "{\"map\":{\"a\":1}}");
    }

    [Fact]
    public void OrderedListOfBuiltInLeaves_IsSupported()
    {
        var domain = DomainWith(e => e.RegisterEventType<ListEvent>());
        var json = domain.EventTypes.SerializeEventPayload(new ListEvent(new List<string> { "a", "b" }));
        var r = OperationFingerprint.ComputeCanonical(
            "s", "k", domain.EventTypes, nameof(ListEvent), Encoding.UTF8.GetBytes(json), Array.Empty<string>());
        Assert.True(r.IsSuccess);
    }

    // ---- Real conditional-executor invocation with an alternating options-level primitive converter ----

    [Fact]
    public async Task OptionsLevelAlternatingConverter_RealExecutor_FailsClosed_NoWrite_NoTwoFingerprints()
    {
        const string payloadSentinel = "PAYLOAD_SENTINEL_7a1c_DO_NOT_LEAK";
        const string keySentinel = "KEY_SENTINEL_3f9e_DO_NOT_LEAK";
        AlternatingStringConverter.Reset();
        var domain = DomainWithOptionsConverter();
        var store = new InMemoryConditionalEventStore(domain.EventTypes);
        var executor = new GeneralSekibanExecutor(store, new InMemoryObjectAccessor(store, domain), domain);
        var options = new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification(keySentinel) };

        var handlerInvocations = 0;
        Func<ShapeCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handler = (_, ctx) =>
        {
            handlerInvocations++;
            return ctx.AppendEvent(new PlainStringEvent(payloadSentinel), new BoundaryTag("t"));
        };

        var first = await executor.ExecuteAsync(new ShapeCommand(), handler, options);
        var second = await executor.ExecuteAsync(new ShapeCommand(), handler, options);

        // PERMITTED invocations, asserted with EXACT counts: the handler ran exactly twice, and the alternating converter
        // emitted exactly twice — once per attempt when the executor serialized the candidate.
        Assert.Equal(2, handlerInvocations);
        Assert.Equal(2, AlternatingStringConverter.WriteCalls);
        Assert.Equal(2, AlternatingStringConverter.Emitted.Count);
        // Non-vacuous: the two emitted candidate values are genuinely DISTINCT (a deterministic converter emitting the
        // same output on both calls would fail this — that is the mutation this test kills).
        Assert.NotEqual(AlternatingStringConverter.Emitted[0], AlternatingStringConverter.Emitted[1]);

        // FORBIDDEN side effects, all absent: both attempts fail with the sanitized canonicalization error (secret-safe:
        // no inner exception), the store never reached the durable-write STEP (AppendAttempts == 0) let alone a
        // successful write (WriteCalls == 0), and no event/receipt exists — so no fingerprint was produced and two
        // differing fingerprints for one key are impossible.
        foreach (var result in new[] { first, second })
        {
            Assert.False(result.IsSuccess);
            var ex = Assert.IsType<OperationCanonicalizationException>(result.GetException());
            Assert.Null(ex.InnerException);
            // Neither the payload value nor the idempotency key leaks anywhere in the observable exception graph.
            ExceptionGraphSecretAssert.ContainsNoneOf(ex, payloadSentinel, keySentinel);
        }

        // Non-vacuous secret check: the sentinels really were in play (the emitted candidates embed the payload value).
        Assert.All(AlternatingStringConverter.Emitted, e => Assert.Contains(payloadSentinel, e, StringComparison.Ordinal));

        Assert.Equal(0, store.AppendAttempts);
        Assert.Equal(0, store.WriteCalls);
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    private static DcbDomainTypes DomainWith(params Action<SimpleEventTypes>[] registrations)
    {
        var d = DomainType.GetDomainTypes();
        foreach (var r in registrations)
        {
            r((SimpleEventTypes)d.EventTypes);
        }

        return d;
    }

    private static DcbDomainTypes DomainWithOptionsConverter()
    {
        var eventTypes = new SimpleEventTypes(
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                Converters = { new AlternatingStringConverter() }
            });
        eventTypes.RegisterEventType<PlainStringEvent>();
        var tagTypes = new SimpleTagTypes();
        tagTypes.RegisterTagGroupType<BoundaryTag>();
        return new DcbDomainTypes(
            eventTypes,
            tagTypes,
            new SimpleTagProjectorTypes(),
            new SimpleTagStatePayloadTypes(),
            new SimpleMultiProjectorTypes(),
            new SimpleQueryTypes(),
            new JsonSerializerOptions());
    }

    private static void AssertRejected(DcbDomainTypes domain, string eventName, string payloadJson)
    {
        var r = OperationFingerprint.ComputeCanonical(
            "s", "k", domain.EventTypes, eventName, Encoding.UTF8.GetBytes(payloadJson), Array.Empty<string>());
        Assert.False(r.IsSuccess);
        Assert.IsType<OperationCanonicalizationException>(r.GetException());
    }

    private record ShapeCommand : ICommand;

    private record BoundaryTag(string Id) : IStringTagGroup<BoundaryTag>
    {
        public static string TagGroupName => "Boundary";
        public static BoundaryTag FromContent(string content) => new(content);
        public bool IsConsistencyTag() => false;
        public string GetId() => Id;
    }

    // An ordinary object whose string leaf is bound by an OPTIONS-LEVEL custom converter (see DomainWithOptionsConverter).
    private record PlainStringEvent(string Field) : IEventPayload;

    // A type-level custom converter on the whole event (root Kind=None).
    [JsonConverter(typeof(RootConverter))]
    private record RootConverterEvent(string Value) : IEventPayload;

    // A property-level custom converter on an otherwise-allowed string leaf.
    private record PropertyConverterEvent(
        [property: JsonConverter(typeof(AlternatingStringConverter))]
        string Name) : IEventPayload;

    // A type-level custom converter on an enum leaf.
    private record EnumConverterEvent(Color Color) : IEventPayload;

    [JsonConverter(typeof(CustomColorConverter))]
    private enum Color { Red, Green }

    private record SetEvent(HashSet<string> Items) : IEventPayload;

    private record MapEvent(Dictionary<string, int> Map) : IEventPayload;

    private record ListEvent(List<string> Items) : IEventPayload;

    /// <summary>
    ///     Emits a valid JSON string that DIFFERS on every write; the boundary must ensure it never fingerprints. Records
    ///     each emitted value so a test can prove the two attempts genuinely produced DISTINCT candidates (non-vacuous).
    /// </summary>
    private sealed class AlternatingStringConverter : JsonConverter<string>
    {
        private static int _writeCalls;
        private static readonly object Sync = new();
        public static List<string> Emitted { get; } = new();
        public static int WriteCalls => Volatile.Read(ref _writeCalls);

        public static void Reset()
        {
            Interlocked.Exchange(ref _writeCalls, 0);
            lock (Sync) Emitted.Clear();
        }

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString() ?? string.Empty;

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            var emitted = $"{value}#{Interlocked.Increment(ref _writeCalls)}";
            lock (Sync) Emitted.Add(emitted);
            writer.WriteStringValue(emitted);
        }
    }

    private sealed class RootConverter : JsonConverter<RootConverterEvent>
    {
        public override RootConverterEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return new RootConverterEvent(doc.RootElement.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "");
        }

        public override void Write(Utf8JsonWriter writer, RootConverterEvent value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("value", value.Value);
            writer.WriteEndObject();
        }
    }

    private sealed class CustomColorConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Enum.Parse<Color>(reader.GetString() ?? nameof(Color.Red));

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
