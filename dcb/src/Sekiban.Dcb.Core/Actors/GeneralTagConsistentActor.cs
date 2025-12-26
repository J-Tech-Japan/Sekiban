using ResultBoxes;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Collections.Concurrent;
using System.Globalization;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     General implementation of ITagConsistentActorCommon
///     Manages tag write reservations to ensure consistency
///     Supports lazy initialization by catching up from event store
///     Can be used with different actor frameworks (InMemory, Orleans, Dapr)
/// </summary>
public class GeneralTagConsistentActor : ITagConsistentActorCommon
{
    private readonly ConcurrentDictionary<string, TagWriteReservation> _activeReservations = new();
    private readonly SemaphoreSlim _catchUpLock = new(1, 1);
    private readonly DcbDomainTypes _domainTypes; // Non-nullable per new requirement
    private readonly IEventStore? _eventStore;
    private readonly TagConsistentActorOptions _options;
    private readonly SemaphoreSlim _reservationLock = new(1, 1);
    private readonly string _tagName;
    private volatile bool _catchUpCompleted;
    private string _latestSortableUniqueId = "";

    public GeneralTagConsistentActor(
        string tagName,
        IEventStore? eventStore,
        TagConsistentActorOptions options,
        DcbDomainTypes domainTypes)
    {
        _tagName = tagName ?? throw new ArgumentNullException(nameof(tagName));
        _eventStore = eventStore;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
    }

    public Task<string> GetTagActorIdAsync() => Task.FromResult(_tagName);

    public async Task<ResultBox<string>> GetLatestSortableUniqueIdAsync()
    {
        try
        {
            // Ensure catch-up is completed before acquiring lock
            await EnsureCatchUpCompletedAsync();

            await _reservationLock.WaitAsync();
            try
            {
                return ResultBox.FromValue(_latestSortableUniqueId);
            }
            finally
            {
                _reservationLock.Release();
            }
        }
        catch (Exception ex)
        {
            return ResultBox.Error<string>(ex);
        }
    }

    public async Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string lastSortableUniqueId)
    {
        // Ensure catch-up is completed before acquiring lock
        await EnsureCatchUpCompletedAsync();

        await _reservationLock.WaitAsync();
        try
        {

            // Clean up expired reservations
            CleanupExpiredReservations();

            // Check if there are any active reservations
            if (_activeReservations.Any())
            {
                return ResultBox.Error<TagWriteReservation>(
                    new Exception($"Tag {await GetTagActorIdAsync()} is currently reserved"));
            }

            // Check for optimistic concurrency - if a specific version was requested, it must match
            if (!string.IsNullOrEmpty(lastSortableUniqueId) &&
                !string.IsNullOrEmpty(_latestSortableUniqueId) &&
                lastSortableUniqueId != _latestSortableUniqueId)
            {
                return ResultBox.Error<TagWriteReservation>(
                    new Exception(
                        $"Tag {await GetTagActorIdAsync()} has been modified. Expected version: {lastSortableUniqueId}, Current version: {_latestSortableUniqueId}"));
            }

            // Update the latest sortable unique ID if provided
            if (!string.IsNullOrEmpty(lastSortableUniqueId))
            {
                _latestSortableUniqueId = lastSortableUniqueId;
            }

            // Create new reservation
            var reservationCode = Guid.NewGuid().ToString();
            var expiredUtc = DateTime.UtcNow.AddSeconds(_options.CancellationWindowSeconds);
            var reservation = new TagWriteReservation(
                reservationCode,
                expiredUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
                await GetTagActorIdAsync());

            _activeReservations[reservationCode] = reservation;

            return ResultBox.FromValue(reservation);
        }
        finally
        {
            _reservationLock.Release();
        }
    }

    public async Task<bool> ConfirmReservationAsync(TagWriteReservation reservation)
    {
        if (reservation == null) return false;

        // Ensure catch-up is completed before acquiring lock
        await EnsureCatchUpCompletedAsync();

        await _reservationLock.WaitAsync();
        try
        {

            if (_activeReservations.TryRemove(reservation.ReservationCode, out var existingReservation))
            {
                // Verify it's the same reservation
                if (existingReservation.Equals(reservation))
                {
                    // After confirming reservation, force a re-catch up to get the latest state
                    _catchUpCompleted = false;
                    return true;
                }
                else
                {
                    // Put it back if it doesn't match
                    _activeReservations[reservation.ReservationCode] = existingReservation;
                    return false;
                }
            }

            return false;
        }
        finally
        {
            _reservationLock.Release();
        }
    }

    public async Task<bool> CancelReservationAsync(TagWriteReservation reservation)
    {
        if (reservation == null) return false;

        // Ensure catch-up is completed before acquiring lock
        await EnsureCatchUpCompletedAsync();

        await _reservationLock.WaitAsync();
        try
        {

            return _activeReservations.TryRemove(reservation.ReservationCode, out _);
        }
        finally
        {
            _reservationLock.Release();
        }
    }

    private async Task EnsureCatchUpCompletedAsync()
    {
        if (_catchUpCompleted)
        {
            return;
        }

        await _catchUpLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_catchUpCompleted)
            {
                return;
            }

            if (_eventStore == null)
            {
                _catchUpCompleted = true;
                return;
            }

            // Catch up from event store
            await CatchUpFromEventStoreAsync();
            _catchUpCompleted = true;
        }
        finally
        {
            _catchUpLock.Release();
        }
    }

    private async Task CatchUpFromEventStoreAsync()
    {
        try
        {
            // Parse tag name using ITagTypes from DcbDomainTypes instead of GenericTag
            var tag = _domainTypes.TagTypes.GetTag(_tagName);

            // Get the latest tag state
            var latestTagResult = await _eventStore!.GetLatestTagAsync(tag);
            if (latestTagResult.IsSuccess)
            {
                var tagState = latestTagResult.GetValue();

                // Update the latest sortable unique ID with proper synchronization
                await _reservationLock.WaitAsync();
                try
                {
                    _latestSortableUniqueId = tagState.LastSortedUniqueId;
                }
                finally
                {
                    _reservationLock.Release();
                }
            }
            // No tag exists yet or error reading, which is fine
            // The actor starts with empty state
        }
        catch
        {
            // If there's any error during catch-up, we still mark as completed
            // to avoid blocking operations
        }
    }

    private void CleanupExpiredReservations()
    {
        var now = DateTime.UtcNow;
        var expiredCodes = _activeReservations
            .Where(kvp => DateTime.Parse(
                    kvp.Value.ExpiredUTC,
                    null,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal) <
                now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var code in expiredCodes)
        {
            _activeReservations.TryRemove(code, out _);
        }
    }

    /// <summary>
    ///     Gets the current active reservations (for testing purposes)
    /// </summary>
    public async Task<IEnumerable<TagWriteReservation>> GetActiveReservationsAsync()
    {
        // Ensure catch-up is completed before acquiring lock
        await EnsureCatchUpCompletedAsync();

        await _reservationLock.WaitAsync();
        try
        {
            CleanupExpiredReservations();
            return _activeReservations.Values.ToList();
        }
        finally
        {
            _reservationLock.Release();
        }
    }
}
