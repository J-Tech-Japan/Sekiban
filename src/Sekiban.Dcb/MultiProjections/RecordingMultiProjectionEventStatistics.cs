using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
/// Debug implementation that records detailed delivery statistics in-memory.
/// </summary>
public class RecordingMultiProjectionEventStatistics : IMultiProjectionEventStatistics
{
    private readonly Dictionary<Guid, int> _eventDeliveryCount = new();
    private long _totalDeliveries;
    private long _duplicateDeliveries;

    private readonly Dictionary<Guid, int> _streamDeliveryCount = new();
    private long _streamDeliveries;

    private readonly Dictionary<Guid, int> _catchUpDeliveryCount = new();
    private long _catchUpDeliveries;

    public void RecordStreamBatch(IEnumerable<Event> events)
    {
        foreach (var evt in events)
        {
            _totalDeliveries++;
            if (_eventDeliveryCount.TryGetValue(evt.Id, out var c))
            {
                _eventDeliveryCount[evt.Id] = c + 1;
                _duplicateDeliveries++;
            }
            else
            {
                _eventDeliveryCount[evt.Id] = 1;
            }

            _streamDeliveries++;
            if (_streamDeliveryCount.TryGetValue(evt.Id, out var sc))
            {
                _streamDeliveryCount[evt.Id] = sc + 1;
            }
            else
            {
                _streamDeliveryCount[evt.Id] = 1;
            }
        }
    }

    public void RecordCatchUpBatch(IEnumerable<Event> events)
    {
        foreach (var evt in events)
        {
            _totalDeliveries++;
            if (_eventDeliveryCount.TryGetValue(evt.Id, out var c))
            {
                _eventDeliveryCount[evt.Id] = c + 1;
                _duplicateDeliveries++;
            }
            else
            {
                _eventDeliveryCount[evt.Id] = 1;
            }

            _catchUpDeliveries++;
            if (_catchUpDeliveryCount.TryGetValue(evt.Id, out var cc))
            {
                _catchUpDeliveryCount[evt.Id] = cc + 1;
            }
            else
            {
                _catchUpDeliveryCount[evt.Id] = 1;
            }
        }
    }

    public (int totalUnique, long totalDeliveries, long duplicateDeliveries, int eventsWithMultipleDeliveries, int maxDeliveryCount, double averageDeliveryCount, int streamUnique, long streamDeliveries, int catchUpUnique, long catchUpDeliveries, string? message) Snapshot()
    {
        var totalUnique = _eventDeliveryCount.Count;
        var maxDelivery = totalUnique > 0 ? _eventDeliveryCount.Values.Max() : 0;
        var average = totalUnique > 0 ? (double)_totalDeliveries / totalUnique : 0d;
        return (
            totalUnique,
            _totalDeliveries,
            _duplicateDeliveries,
            _eventDeliveryCount.Count(kvp => kvp.Value > 1),
            maxDelivery,
            average,
            _streamDeliveryCount.Count,
            _streamDeliveries,
            _catchUpDeliveryCount.Count,
            _catchUpDeliveries,
            null
        );
    }
}

