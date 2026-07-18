using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.TestSupport;

/// <summary>
///     Shared marker payload for SEK-G16 conditional-append provider tests. A single-string record so its canonical
///     fingerprint is stable and its shape is admitted by the payload-shape validator.
/// </summary>
public record ConditionalMarkerEvent(string Value) : IEventPayload;

/// <summary>Shared tag group for SEK-G16 conditional-append provider tests.</summary>
public record ConditionalMarkerTag(string Id) : IStringTagGroup<ConditionalMarkerTag>
{
    public static string TagGroupName => "Migration";
    public static ConditionalMarkerTag FromContent(string content) => new(content);
    public bool IsConsistencyTag() => false;
    public string GetId() => Id;
}

/// <summary>
///     The provider-agnostic SEK-G16 conditional-append outcome-machine assertions, so every provider's test proves the
///     identical observable contract without repeating the assertion bodies. A provider test supplies its live store, a
///     marker-event factory, and a "count durable events" probe; provider-specific setup (store construction, real vs
///     faked backend) stays in the provider test.
/// </summary>
public static class ConditionalAppendScenarios
{
    /// <summary>Registers the shared marker event/tag into a domain (idempotently — the tag registration throws on a duplicate).</summary>
    public static DcbDomainTypes RegisterMarker(DcbDomainTypes domain)
    {
        ((SimpleEventTypes)domain.EventTypes).RegisterEventType<ConditionalMarkerEvent>();
        try
        {
            ((SimpleTagTypes)domain.TagTypes).RegisterTagGroupType<ConditionalMarkerTag>();
        }
        catch (InvalidOperationException)
        {
            // A shared domain instance already has it registered from an earlier test.
        }
        return domain;
    }

    /// <summary>Builds a marker <see cref="SerializableEvent" /> with a fresh id/sortable-id and one tag.</summary>
    public static SerializableEvent Marker(DcbDomainTypes domain, string value) =>
        new Event(new ConditionalMarkerEvent(value), SortableUniqueId.GenerateNew(), nameof(ConditionalMarkerEvent),
                Guid.CreateVersion7(), new EventMetadata("c", "c", "u"), new List<string> { "Migration:once" })
            .ToSerializableEvent(domain.EventTypes);

    public static void AssertCapability(IWriteConditionCapabilityProvider store) =>
        Assert.True(store.DescribeWriteConditions().Supports(WriteConditionKind.SingleEventUniqueKey));

    /// <summary>First append wins; a same-operation retry returns the ORIGINAL winner's receipt and writes nothing new.</summary>
    public static async Task AssertFirstAppendWins_SameOpRetryIsIdempotent(
        IConditionalEventStore store,
        DcbDomainTypes domain,
        string key,
        Func<Task<int>> durableCount)
    {
        var first = (await store.AppendIfUniqueAsync(new ConditionalAppendRequest(key, Marker(domain, "v")))).GetValue();
        var second = (await store.AppendIfUniqueAsync(new ConditionalAppendRequest(key, Marker(domain, "v")))).GetValue();

        Assert.Equal(ConditionalAppendStatus.Appended, first.Status);
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, second.Status);
        Assert.Equal(first.WinnerEventId, second.WinnerEventId);
        Assert.Equal(first.WinnerSortableUniqueId, second.WinnerSortableUniqueId);
        Assert.Equal(first.OperationFingerprint, second.OperationFingerprint);
        Assert.Equal(1, await durableCount());
    }

    /// <summary>A different operation under the same key is a key-reuse conflict carrying the real provider exception.</summary>
    public static async Task AssertDifferentOperationIsKeyReuseConflict_WithProviderCause(
        IConditionalEventStore store,
        DcbDomainTypes domain,
        string key,
        Func<Task<int>> durableCount)
    {
        Assert.True((await store.AppendIfUniqueAsync(new ConditionalAppendRequest(key, Marker(domain, "first")))).IsSuccess);
        var conflict = await store.AppendIfUniqueAsync(new ConditionalAppendRequest(key, Marker(domain, "DIFFERENT")));

        Assert.False(conflict.IsSuccess);
        var ex = Assert.IsType<KeyReuseConflictException>(conflict.GetException());
        Assert.NotNull(ex.InnerException); // the real provider conflict is preserved as the diagnostic cause
        Assert.Equal(1, await durableCount());
    }

    /// <summary>N writers of the same operation converge on one winner and exactly one durable event.</summary>
    public static async Task AssertNWritersConverge(
        IConditionalEventStore store,
        DcbDomainTypes domain,
        string key,
        int writers,
        Func<Task<int>> durableCount)
    {
        var attempts = await Task.WhenAll(
            Enumerable.Range(0, writers).Select(_ =>
                store.AppendIfUniqueAsync(new ConditionalAppendRequest(key, Marker(domain, "payload")))));

        var receipts = attempts.Where(r => r.IsSuccess).Select(r => r.GetValue()).ToList();
        Assert.Equal(writers, receipts.Count); // no writer errored
        Assert.Equal(1, receipts.Count(r => r.Status == ConditionalAppendStatus.Appended));
        Assert.Equal(writers - 1, receipts.Count(r => r.Status == ConditionalAppendStatus.AlreadyCommittedSameOperation));
        Assert.Single(receipts.Select(r => r.WinnerEventId).Distinct());
        Assert.Single(receipts.Select(r => r.OperationFingerprint).Distinct());
        Assert.Equal(1, await durableCount());
    }
}
