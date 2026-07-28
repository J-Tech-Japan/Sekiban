using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

// SEK-G20 shared domain + helpers for the two-cluster PRODUCT proofs. The InMemory-reference test and the
// Postgres-Testcontainers authoritative test both drive this ONE first-event-wins projector so the harness (parked stale
// writer, tombstone barrier, restart convergence) is proven identically against both stores without copy-paste.

public record CreatedWithId(string Id, string Value) : IEventPayload;
public record WinnerResult(string Value);
public record WinnerRow(string Id, string Value);

public record WinnerQuery(string Id) : IMultiProjectionQuery<FirstWinsProjector, WinnerQuery, WinnerResult>
{
    public static ResultBox<WinnerResult> HandleQuery(FirstWinsProjector p, WinnerQuery q, IQueryContext c) =>
        ResultBox.FromValue(new WinnerResult(p.Winners.TryGetValue(q.Id, out var v) ? v : string.Empty));
}

public record WinnerListQuery : IMultiProjectionListQuery<FirstWinsProjector, WinnerListQuery, WinnerRow>, IQueryPagingParameter
{
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }
    public static ResultBox<IEnumerable<WinnerRow>> HandleFilter(FirstWinsProjector p, WinnerListQuery q, IQueryContext c) =>
        ResultBox.FromValue(p.Winners.Select(kv => new WinnerRow(kv.Key, kv.Value)));
    public static ResultBox<IEnumerable<WinnerRow>> HandleSort(IEnumerable<WinnerRow> f, WinnerListQuery q, IQueryContext c) =>
        ResultBox.FromValue(f.OrderBy(r => r.Id, StringComparer.Ordinal).AsEnumerable());
}

[global::Orleans.GenerateSerializer]
public record FirstWinsProjector : IMultiProjector<FirstWinsProjector>
{
    [global::Orleans.Id(0)]
    public Dictionary<string, string> Winners { get; init; } = new();
    public static string MultiProjectorName => "g20-retro-first-wins";
    public static string MultiProjectorVersion => "1.0.0";
    public static FirstWinsProjector GenerateInitialPayload() => new();
    public static ResultBox<FirstWinsProjector> Project(
        FirstWinsProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold)
    {
        if (ev.Payload is CreatedWithId created)
        {
            if (payload.Winners.ContainsKey(created.Id)) return ResultBox.FromValue(payload);
            return ResultBox.FromValue(payload with { Winners = new Dictionary<string, string>(payload.Winners) { [created.Id] = created.Value } });
        }
        return ResultBox.FromValue(payload);
    }
}

/// <summary>Shared builders + poll/query helpers for the two-cluster product proofs.</summary>
public static class G20Shared
{
    public static DcbDomainTypes BuildDomain()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<CreatedWithId>("CreatedWithId");
        var mp = new SimpleMultiProjectorTypes();
        mp.RegisterProjector<FirstWinsProjector>();
        var q = new SimpleQueryTypes();
        q.RegisterQuery<WinnerQuery>();
        q.RegisterListQuery<WinnerListQuery>();
        return new DcbDomainTypes(eventTypes, new SimpleTagTypes(), new SimpleTagProjectorTypes(),
            new SimpleTagStatePayloadTypes(), mp, q, new JsonSerializerOptions());
    }

    public static Event CreateEvent(IEventPayload payload, DateTime timestamp) => new(
        payload, SortableUniqueId.Generate(timestamp, Guid.NewGuid()), payload.GetType().Name,
        Guid.NewGuid(), new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"), new List<string>());

    public static SerializableEvent ToSerializable(Event ev) => new(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ev.Payload, ev.Payload.GetType())),
        ev.SortableUniqueIdValue, ev.Id, ev.EventMetadata, ev.Tags.ToList(), ev.EventType);

    public static string WinnerOf(MultiProjectionState state) =>
        ((FirstWinsProjector)state.Payload).Winners.TryGetValue("team-1", out var v) ? v : string.Empty;

    public static async Task<string> WinnerAsync(IMultiProjectionGrain grain)
    {
        var rb = await grain.GetStateAsync();
        return rb.IsSuccess ? (((FirstWinsProjector)rb.GetValue().Payload).Winners.TryGetValue("team-1", out var v) ? v : "") : "(fail)";
    }

    // The first query drives the durable rebuild (fail-closed until it reaches the head), so poll generously and TOLERATE
    // fail-closed errors — do NOT block on waitForCatchUp (that can deadlock against the barrier). Nudge periodically.
    public static async Task<string> PollWinnerAsync(IMultiProjectionGrain grain, string expected)
    {
        for (var i = 0; i < 200; i++)
        {
            var rb = await grain.GetStateAsync(canGetUnsafeState: true, waitForCatchUp: false);
            if (rb.IsSuccess && ((FirstWinsProjector)rb.GetValue().Payload).Winners.TryGetValue("team-1", out var v) && v == expected)
            {
                return v;
            }
            if (i % 15 == 14)
            {
                try { await grain.RefreshAsync(); } catch { /* fail-closed during rebuild is expected */ }
            }
            await Task.Delay(200);
        }
        return await WinnerAsync(grain);
    }

    public static async Task<MultiProjectionState> PollSafeWinnerAsync(IMultiProjectionGrain grain, string expected)
    {
        MultiProjectionState last = null!;
        for (var i = 0; i < 80; i++)
        {
            var rb = await grain.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false);
            if (rb.IsSuccess)
            {
                last = rb.GetValue();
                if (((FirstWinsProjector)last.Payload).Winners.TryGetValue("team-1", out var v) && v == expected && last.IsSafeState)
                {
                    return last;
                }
            }
            await Task.Delay(200);
        }
        return last;
    }

    public static async Task PollUnsafeContainsAsync(IMultiProjectionGrain grain, string id)
    {
        for (var i = 0; i < 100; i++)
        {
            var rb = await grain.GetStateAsync(canGetUnsafeState: true, waitForCatchUp: false);
            if (rb.IsSuccess && ((FirstWinsProjector)rb.GetValue().Payload).Winners.ContainsKey(id))
            {
                return;
            }
            await Task.Delay(50);
        }
    }

    public static async Task AssertScalarAndListAsync(ISekibanExecutor executor, string expectedWinner)
    {
        var scalar = await executor.QueryAsync(new WinnerQuery("team-1"));
        Assert.True(scalar.IsSuccess, scalar.IsSuccess ? "" : scalar.GetException().ToString());
        Assert.Equal(expectedWinner, scalar.GetValue().Value);

        var list = await executor.QueryAsync(new WinnerListQuery());
        Assert.True(list.IsSuccess, list.IsSuccess ? "" : list.GetException().ToString());
        var row = Assert.Single(list.GetValue().Items.ToList(), r => r.Id == "team-1");
        Assert.Equal(expectedWinner, row.Value);
    }
}

/// <summary>
///     A content-addressed in-memory blob accessor: the storage KEY is the SHA-256 of the payload, so identical bytes map
///     to the same key and any change of the offloaded snapshot bytes changes the key. Lets a test assert that a rejected
///     stale writer left BOTH the checkpoint row AND the offloaded blob byte-identical.
/// </summary>
public sealed class ContentAddressedBlobAccessor : IBlobStorageSnapshotAccessor
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new();
    public string ProviderName => "InMemoryContentAddressed";

    public async Task<string> WriteAsync(Stream data, string projectorName, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await data.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        var bytes = ms.ToArray();
        var key = Convert.ToHexString(SHA256.HashData(bytes));
        _blobs[key] = bytes;
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
        _blobs.TryGetValue(key, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
            : throw new InvalidOperationException($"blob {key} not found");

    public byte[]? TryGet(string key) => _blobs.TryGetValue(key, out var b) ? b : null;
}
