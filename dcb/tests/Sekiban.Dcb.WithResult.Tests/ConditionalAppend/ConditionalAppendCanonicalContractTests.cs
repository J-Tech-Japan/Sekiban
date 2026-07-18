using Dcb.Domain;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G15 frozen canonical-fingerprint contract (derivation v2 / canonicalization v1). These are golden vectors with
///     LITERAL expected digests, so any drift in the version, domain separator, field order, length-prefix framing, or
///     canonicalization algorithm changes the digest and fails here — not just relative equality. Plus non-vacuous
///     cross-pipeline vectors (nested reordering, arrays, numbers, unicode/escaping, NFC keys, ordered-tag
///     order/duplicates/case, authoritative type) and deterministic fail-closed on unsupported shapes.
/// </summary>
public class ConditionalAppendCanonicalContractTests
{
    private static DcbDomainTypes Domain()
    {
        var d = DomainType.GetDomainTypes();
        ((SimpleEventTypes)d.EventTypes).RegisterEventType<CanonEvent>();
        ((SimpleEventTypes)d.EventTypes).RegisterEventType<OtherCanonEvent>();
        ((SimpleEventTypes)d.EventTypes).RegisterEventType<HostileEvent>();
        return d;
    }



    // ---- Frozen literal digests (fail on any derivation/canonicalization drift) ----

    [Fact]
    public void ComputeFromCanonical_FrozenDigest()
    {
        var digest = OperationFingerprint.ComputeFromCanonical(
            "svc-1", "key-1", "My.Type",
            OperationFingerprint.CanonicalizeJson("{\"a\":1,\"b\":\"x\"}"),
            new[] { "A:1", "B:2" });
        Assert.Equal("4ef47d7068f7a740f23cca54401ad9c14ba1d236a4a3965c8e1f0cd813c994d6", digest);
    }

    [Fact]
    public void CanonicalizeJson_FrozenOutput_SortsKeysRecursively_PreservesArrayOrder()
    {
        var canonical = Encoding.UTF8.GetString(
            OperationFingerprint.CanonicalizeJson("{ \"b\":[3,1,2], \"a\":{\"y\":1,\"x\":2} }"));
        Assert.Equal("{\"a\":{\"x\":2,\"y\":1},\"b\":[3,1,2]}", canonical);
    }

    [Fact]
    public void ComputeCanonical_EndToEnd_FrozenDigest()
    {
        var domain = Domain();
        var json = domain.EventTypes.SerializeEventPayload(new CanonEvent("hello", 7, new[] { "z", "a" }));
        var digest = OperationFingerprint.ComputeCanonical(
            "svc-1", "key-1", domain.EventTypes, nameof(CanonEvent),
            Encoding.UTF8.GetBytes(json), new[] { "Tag:1" }).GetValue();
        Assert.Equal("9abe33d679a894b12ea1f4a5184fdcbf71378465770a7e75250aff496ed021bd", digest);
    }

    // ---- Canonicalization vectors ----

    [Fact]
    public void NestedObjectPropertyReorder_IsCanonicallyEqual()
    {
        var a = OperationFingerprint.CanonicalizeJson("{\"a\":{\"p\":1,\"q\":2},\"b\":3}");
        var b = OperationFingerprint.CanonicalizeJson("{\"b\":3,\"a\":{\"q\":2,\"p\":1}}");
        Assert.Equal(Encoding.UTF8.GetString(a), Encoding.UTF8.GetString(b));
    }

    [Fact]
    public void ArrayOrder_IsSignificant()
    {
        var a = OperationFingerprint.CanonicalizeJson("{\"a\":[1,2,3]}");
        var b = OperationFingerprint.CanonicalizeJson("{\"a\":[3,2,1]}");
        Assert.NotEqual(Encoding.UTF8.GetString(a), Encoding.UTF8.GetString(b));
    }

    [Fact]
    public void NumericRepresentation_IsPreservedAsWritten()
    {
        // The canonical form preserves the numeric token as the (domain) serializer emitted it; 1 and 1.0 are distinct.
        var a = OperationFingerprint.CanonicalizeJson("{\"n\":1}");
        var b = OperationFingerprint.CanonicalizeJson("{\"n\":1.0}");
        Assert.NotEqual(Encoding.UTF8.GetString(a), Encoding.UTF8.GetString(b));
    }

    [Fact]
    public void UnicodeEscape_AndLiteral_CanonicalizeEqually()
    {
        var escaped = OperationFingerprint.CanonicalizeJson("{\"s\":\"\\u00e9\"}");
        var literal = OperationFingerprint.CanonicalizeJson("{\"s\":\"é\"}");
        Assert.Equal(Encoding.UTF8.GetString(escaped), Encoding.UTF8.GetString(literal));
    }

    [Fact]
    public void IdempotencyKey_NfcEquivalentForms_ProduceSameFingerprint()
    {
        // Composed "\u00e9" vs decomposed "e" + combining acute (U+0301) — NFC makes them the same key.
        var composed = "caf\u00e9";
        var decomposed = "cafe\u0301";
        Assert.NotEqual(composed, decomposed); // genuinely different strings going in
        var f1 = OperationFingerprint.ComputeFromCanonical("svc", composed, "T", OperationFingerprint.CanonicalizeJson("{}"), Array.Empty<string>());
        var f2 = OperationFingerprint.ComputeFromCanonical("svc", decomposed, "T", OperationFingerprint.CanonicalizeJson("{}"), Array.Empty<string>());
        Assert.Equal(f1, f2);
    }

    // ---- Ordered-tag semantics: order-insensitive, duplicate-significant, case-significant ----

    [Fact]
    public void Tags_OrderInsensitive()
    {
        var canon = OperationFingerprint.CanonicalizeJson("{}");
        Assert.Equal(
            OperationFingerprint.ComputeFromCanonical("s", "k", "T", canon, new[] { "A:1", "B:2" }),
            OperationFingerprint.ComputeFromCanonical("s", "k", "T", canon, new[] { "B:2", "A:1" }));
    }

    [Fact]
    public void Tags_DuplicatesAreSignificant()
    {
        var canon = OperationFingerprint.CanonicalizeJson("{}");
        Assert.NotEqual(
            OperationFingerprint.ComputeFromCanonical("s", "k", "T", canon, new[] { "A:1" }),
            OperationFingerprint.ComputeFromCanonical("s", "k", "T", canon, new[] { "A:1", "A:1" }));
    }

    [Fact]
    public void Tags_AreCaseSignificant()
    {
        var canon = OperationFingerprint.CanonicalizeJson("{}");
        Assert.NotEqual(
            OperationFingerprint.ComputeFromCanonical("s", "k", "T", canon, new[] { "a:1" }),
            OperationFingerprint.ComputeFromCanonical("s", "k", "T", canon, new[] { "A:1" }));
    }

    [Fact]
    public void AuthoritativeEventType_IsPartOfIdentity()
    {
        var canon = OperationFingerprint.CanonicalizeJson("{}");
        Assert.NotEqual(
            OperationFingerprint.ComputeFromCanonical("s", "k", "Type.A", canon, Array.Empty<string>()),
            OperationFingerprint.ComputeFromCanonical("s", "k", "Type.B", canon, Array.Empty<string>()));
    }

    [Fact]
    public void DifferentRegisteredTypes_SamePayloadShape_DifferentFingerprint()
    {
        var domain = Domain();
        var jsonA = domain.EventTypes.SerializeEventPayload(new CanonEvent("x", 1, Array.Empty<string>()));
        var jsonB = domain.EventTypes.SerializeEventPayload(new OtherCanonEvent("x", 1, Array.Empty<string>()));
        var fa = OperationFingerprint.ComputeCanonical("s", "k", domain.EventTypes, nameof(CanonEvent), Encoding.UTF8.GetBytes(jsonA), Array.Empty<string>()).GetValue();
        var fb = OperationFingerprint.ComputeCanonical("s", "k", domain.EventTypes, nameof(OtherCanonEvent), Encoding.UTF8.GetBytes(jsonB), Array.Empty<string>()).GetValue();
        Assert.NotEqual(fa, fb);
    }

    // ---- Deterministic fail-closed on unsupported shapes ----

    [Fact]
    public void UnregisteredType_FailsClosed()
    {
        var domain = Domain();
        var r = OperationFingerprint.ComputeCanonical("s", "k", domain.EventTypes, "Nope", Encoding.UTF8.GetBytes("{}"), Array.Empty<string>());
        Assert.False(r.IsSuccess);
        Assert.IsType<OperationCanonicalizationException>(r.GetException());
    }

    [Fact]
    public void UnparseablePayload_FailsClosed()
    {
        var domain = Domain();
        var r = OperationFingerprint.ComputeCanonical("s", "k", domain.EventTypes, nameof(CanonEvent), Encoding.UTF8.GetBytes("{ not json "), Array.Empty<string>());
        Assert.False(r.IsSuccess);
        Assert.IsType<OperationCanonicalizationException>(r.GetException());
    }

    // ---- Secret-safe canonicalization failure ----

    [Fact]
    public void HostileDeserializer_SecretsNeverLeakIntoTheErrorGraph()
    {
        const string sentinel = "SENTINEL_SECRET_9c3f_DO_NOT_LEAK";
        var domain = Domain();

        // A payload whose deserialization throws with the sentinel embedded; the key also carries the sentinel.
        var r = OperationFingerprint.ComputeCanonical(
            "svc", sentinel, domain.EventTypes, nameof(HostileEvent),
            Encoding.UTF8.GetBytes($"{{\"Value\":\"{sentinel}\"}}"), new[] { $"Tag:{sentinel}" });

        Assert.False(r.IsSuccess);
        var ex = Assert.IsType<OperationCanonicalizationException>(r.GetException());
        Assert.Null(ex.InnerException); // no chained converter/deserializer exception at all
        AssertNoSecretInExceptionGraph(ex, sentinel);
    }

    private static void AssertNoSecretInExceptionGraph(Exception ex, string sentinel)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            Assert.DoesNotContain(sentinel, current.Message ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(sentinel, current.ToString() ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(sentinel, current.StackTrace ?? string.Empty, StringComparison.Ordinal);
            foreach (System.Collections.DictionaryEntry entry in current.Data)
            {
                Assert.DoesNotContain(sentinel, entry.Key?.ToString() ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain(sentinel, entry.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }

    private record CanonEvent(string Name, int Count, string[] Items) : IEventPayload;

    private record OtherCanonEvent(string Name, int Count, string[] Items) : IEventPayload;

    // Deserializing this throws with the assigned value in the message — a hostile deserializer that would leak the raw
    // payload if the exception were chained into the result.
    private record HostileEvent : IEventPayload
    {
        public string Value
        {
            get => string.Empty;
            init => throw new InvalidOperationException($"hostile deserializer leaked value: {value}");
        }
    }
}
