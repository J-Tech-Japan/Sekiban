using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     Compatibility proof for SEK-G15: the conditional-append feature is strictly additive. Frozen public-API baselines
///     fail if <see cref="IEventStore" />, the core command context, or the positional serialized DTO grow or change; a
///     hand-rolled EXTERNAL event store (a downstream consumer that predates this change) still compiles and works, is
///     never capability-cast, and reports no write-condition capability.
/// </summary>
public class ConditionalAppendCompatibilityTests
{
    // ---- Frozen public-API baselines. These freeze COMPLETE signatures (return type + parameter types + generic
    //      arity), not just member names/counts, so any signature CHANGE — not only an add/remove — fails here. ----

    private static string[] FrozenSignatures(Type type) =>
        type.GetMethods()
            .Select(FormatSignature)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

    private static string FormatSignature(System.Reflection.MethodInfo m)
    {
        var generic = m.IsGenericMethodDefinition ? "`" + m.GetGenericArguments().Length : string.Empty;
        var parameters = string.Join(", ", m.GetParameters().Select(p => $"{Name(p.ParameterType)} {p.Name}"));
        return $"{Name(m.ReturnType)} {m.Name}{generic}({parameters})";
    }

    private static string Name(Type t)
    {
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition().FullName ?? t.GetGenericTypeDefinition().Name;
            var args = string.Join(",", t.GetGenericArguments().Select(Name));
            return $"{def}[{args}]";
        }

        return t.FullName ?? t.Name;
    }

    [Fact]
    public void IEventStore_PublicSignatures_AreFrozen()
    {
        var expected = new[]
        {
            "System.Threading.Tasks.Task`1[ResultBoxes.ResultBox`1[System.Collections.Generic.IEnumerable`1[Sekiban.Dcb.Storage.TagInfo]]] GetAllTagsAsync(System.String tagGroup)",
            "System.Threading.Tasks.Task`1[ResultBoxes.ResultBox`1[System.Collections.Generic.IEnumerable`1[Sekiban.Dcb.Tags.TagStream]]] ReadTagsAsync(Sekiban.Dcb.Tags.ITag tag)",
            "System.Threading.Tasks.Task`1[ResultBoxes.ResultBox`1[System.Collections.Generic.IEnumerable`1[Sekiban.Dcb.Events.SerializableEvent]]] ReadAllSerializableEventsAsync(Sekiban.Dcb.Common.SortableUniqueId since)",
            "System.Threading.Tasks.Task`1[ResultBoxes.ResultBox`1[System.Collections.Generic.IEnumerable`1[Sekiban.Dcb.Events.SerializableEvent]]] ReadAllSerializableEventsAsync(Sekiban.Dcb.Common.SortableUniqueId since, System.Nullable`1[System.Int32] maxCount)",
            "System.Threading.Tasks.Task`1[ResultBoxes.ResultBox`1[System.Collections.Generic.IEnumerable`1[Sekiban.Dcb.Events.SerializableEvent]]] ReadSerializableEventsByTagAsync(Sekiban.Dcb.Tags.ITag tag, Sekiban.Dcb.Common.SortableUniqueId since)",
            "System.Threading.Tasks.Task`1[ResultBoxes.ResultBox`1[Sekiban.Dcb.Events.SerializableEvent]] ReadSerializableEventAsync(System.Guid eventId)",
            "System.Threading.Tasks.Task`1[ResultBoxes.ResultBox`1[Sekiban.Dcb.Tags.TagState]] GetLatestTagAsync(Sekiban.Dcb.Tags.ITag tag)",
            "System.Threading.Tasks.Task`1[ResultBoxes.ResultBox`1[System.Boolean]] TagExistsAsync(Sekiban.Dcb.Tags.ITag tag)",
            "System.Threading.Tasks.Task`1[ResultBoxes.ResultBox`1[System.Int64]] GetEventCountAsync(Sekiban.Dcb.Common.SortableUniqueId since)",
            "System.Threading.Tasks.Task`1[ResultBoxes.ResultBox`1[System.String]] GetLatestSortableUniqueIdAsync()",
            "System.Threading.Tasks.Task`1[ResultBoxes.ResultBox`1[System.ValueTuple`2[System.Collections.Generic.IReadOnlyList`1[Sekiban.Dcb.Events.SerializableEvent],System.Collections.Generic.IReadOnlyList`1[Sekiban.Dcb.Tags.TagWriteResult]]]] WriteSerializableEventsAsync(System.Collections.Generic.IEnumerable`1[Sekiban.Dcb.Events.SerializableEvent] events)"
        }.OrderBy(s => s, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, FrozenSignatures(typeof(IEventStore)));
    }

    [Fact]
    public void SerializedCommitRequest_PositionalSignature_IsFrozen()
    {
        var ctor = typeof(SerializedCommitRequest).GetConstructors().Single();
        var signature = string.Join(", ", ctor.GetParameters().Select(p => $"{Name(p.ParameterType)} {p.Name}"));
        Assert.Equal(
            "System.Collections.Generic.IReadOnlyList`1[Sekiban.Dcb.Events.SerializableEventCandidate] EventCandidates, "
            + "System.Collections.Generic.IReadOnlyList`1[Sekiban.Dcb.Commands.ConsistencyTagEntry] ConsistencyTags",
            signature);
    }

    [Fact]
    public void CoreCommandContext_AppendEventSignatures_AreFrozen()
    {
        var appendEvent = typeof(Sekiban.Dcb.Commands.ICoreCommandContext)
            .GetMethods()
            .Where(m => m.Name == "AppendEvent")
            .Select(FormatSignature)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            "System.Threading.Tasks.Task`1[ResultBoxes.ResultBox`1[Sekiban.Dcb.Events.EventOrNone]] AppendEvent(Sekiban.Dcb.Events.EventPayloadWithTags eventPayloadWithTags)",
            "System.Threading.Tasks.Task`1[ResultBoxes.ResultBox`1[Sekiban.Dcb.Events.EventOrNone]] AppendEvent(Sekiban.Dcb.Events.IEventPayload ev, Sekiban.Dcb.Tags.ITag[] tags)"
        }.OrderBy(s => s, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, appendEvent);
    }

    // The external-consumer compatibility fixture (a legacy IEventStore implementor + legacy-surface usage compiled
    // against the PRODUCED assemblies) lives in a genuinely separate project: dcb/tests/Sekiban.Dcb.LegacyConsumerFixture.
    // Keeping it out of this in-source test assembly is the point — it proves a downstream consumer still compiles.
}
