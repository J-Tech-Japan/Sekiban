using System.Text;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.Primitives;

/// <summary>
///     IMultiProjectionActor adapter for primitive projection runtimes.
/// </summary>
public sealed class PrimitiveMultiProjectionActorAdapter : IMultiProjectionActor
{
    private readonly PrimitiveMultiProjectionActor _actor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly string _projectorName;
    private bool _isCatchedUp = true;
    private Guid _lastEventId = Guid.Empty;
    private string _lastSortableUniqueId = string.Empty;

    public PrimitiveMultiProjectionActorAdapter(
        DcbDomainTypes domainTypes,
        IPrimitiveProjectionHost host,
        string projectorName,
        string instanceKey,
        ILogger? logger = null)
    {
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _projectorName = projectorName ?? throw new ArgumentNullException(nameof(projectorName));
        _actor = new PrimitiveMultiProjectionActor(domainTypes, host, projectorName, logger, instanceKey);
    }

    public async Task AddEventsAsync(
        IReadOnlyList<Event> events,
        bool finishedCatchUp = true,
        EventSource source = EventSource.Unknown)
    {
        _isCatchedUp = finishedCatchUp;

        if (events.Count == 0)
        {
            return;
        }

        var ordered = events
            .GroupBy(e => e.Id)
            .Select(g => g.First())
            .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal)
            .ToList();

        foreach (var ev in ordered)
        {
            await _actor.ApplyEventAsync(ev);
            _lastEventId = ev.Id;
            if (string.IsNullOrEmpty(_lastSortableUniqueId) ||
                string.Compare(ev.SortableUniqueIdValue, _lastSortableUniqueId, StringComparison.Ordinal) > 0)
            {
                _lastSortableUniqueId = ev.SortableUniqueIdValue;
            }
        }
    }

    public async Task AddSerializableEventsAsync(IReadOnlyList<SerializableEvent> events, bool finishedCatchUp = true)
    {
        if (events.Count == 0)
        {
            _isCatchedUp = finishedCatchUp;
            return;
        }

        var list = new List<Event>(events.Count);
        foreach (var se in events)
        {
            var payload =
                _domainTypes.EventTypes.DeserializeEventPayload(
                    se.EventPayloadName,
                    Encoding.UTF8.GetString(se.Payload)) ??
                throw new InvalidOperationException($"Unknown event type: {se.EventPayloadName}");

            var ev = new Event(
                payload,
                se.SortableUniqueIdValue,
                se.EventPayloadName,
                se.Id,
                se.EventMetadata,
                se.Tags);
            list.Add(ev);
        }

        await AddEventsAsync(list, finishedCatchUp);
    }

    public async Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true)
    {
        try
        {
            var snapshot = await _actor.CreateSnapshotAsync();
            var payload = new PrimitiveJsonMultiProjectionPayload(_projectorName, snapshot.StateJson);
            return ResultBox.FromValue(
                new MultiProjectionState(
                    payload,
                    _projectorName,
                    snapshot.ProjectorVersion,
                    snapshot.LastSortableUniqueId ?? string.Empty,
                    _lastEventId,
                    snapshot.Version,
                    _isCatchedUp,
                    true));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<MultiProjectionState>(ex);
        }
    }

    public async Task<ResultBox<SerializableMultiProjectionStateEnvelope>> GetSnapshotAsync(
        bool canGetUnsafeState = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await _actor.CreateSnapshotAsync();
            var bytes = Encoding.UTF8.GetBytes(snapshot.StateJson ?? string.Empty);
            var state = SerializableMultiProjectionState.FromBytes(
                bytes,
                typeof(PrimitiveJsonMultiProjectionPayload).FullName ?? nameof(PrimitiveJsonMultiProjectionPayload),
                _projectorName,
                snapshot.ProjectorVersion,
                snapshot.LastSortableUniqueId ?? string.Empty,
                _lastEventId,
                snapshot.Version,
                _isCatchedUp,
                true,
                bytes.LongLength,
                bytes.LongLength);

            return ResultBox.FromValue(new SerializableMultiProjectionStateEnvelope(false, state, null));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializableMultiProjectionStateEnvelope>(ex);
        }
    }

    public async Task SetSnapshotAsync(
        SerializableMultiProjectionStateEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.IsOffloaded)
        {
            throw new InvalidOperationException(
                "Offloaded snapshots are not supported by the primitive adapter. Restore from the state store first.");
        }

        if (envelope.InlineState == null)
        {
            throw new InvalidOperationException("Inline snapshot missing InlineState");
        }

        await SetCurrentState(envelope.InlineState);
    }

    public Task SetCurrentState(SerializableMultiProjectionState state)
    {
        var payloadBytes = state.GetPayloadBytes();
        var json = payloadBytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(payloadBytes);
        var snapshot = new PrimitiveProjectionSnapshot(
            state.ProjectorName,
            state.ProjectorVersion,
            json,
            state.Version,
            state.LastSortableUniqueId,
            DateTime.UtcNow);

        _lastSortableUniqueId = state.LastSortableUniqueId ?? string.Empty;
        _lastEventId = state.LastEventId;
        _isCatchedUp = state.IsCatchedUp;

        return _actor.RestoreSnapshotAsync(snapshot);
    }

    public Task SetCurrentStateIgnoringVersion(SerializableMultiProjectionState state) => SetCurrentState(state);

    public Task<string> GetSafeLastSortableUniqueIdAsync() =>
        Task.FromResult(_lastSortableUniqueId);

    public SortableUniqueId PeekCurrentSafeWindowThreshold() => SortableUniqueId.MaxValue;

    public Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId) =>
        _actor.IsSortableUniqueIdReceivedAsync(sortableUniqueId);
}
