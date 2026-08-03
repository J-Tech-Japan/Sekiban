using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResultBoxes;
using Sekiban.Dcb.Domains;
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
    private readonly ILogger<GeneralTagConsistentActor> _logger;
    private readonly ITagTypes _tagTypes;
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
        ITagTypes tagTypes,
        ILogger<GeneralTagConsistentActor>? logger = null)
    {
        _tagName = tagName ?? throw new ArgumentNullException(nameof(tagName));
        _eventStore = eventStore;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tagTypes = tagTypes ?? throw new ArgumentNullException(nameof(tagTypes));
        _logger = logger ?? NullLogger<GeneralTagConsistentActor>.Instance;
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

            // SEK-G19: EXACT-MATCH optimistic-concurrency check after null/empty NORMALIZATION, in-lock and post-catch-up. An
            // empty caller version means "I expect this tag to be EMPTY" (a first write) — NOT "skip the check". Comparing
            // the normalized expected against the normalized current (both null/empty collapse to "") covers all five
            // classes: empty/empty pass (first write on an empty tag); empty/non-empty CONFLICT (a second first-write against
            // a tag that already has committed state — the #1085 hole); non-empty/empty CONFLICT (an update expecting a
            // version the tag never had — the secondary hole); non-empty mismatch CONFLICT; non-empty match pass. The
            // active-reservation rejection above is unchanged. GUARANTEE BOUNDARY: this holds at-most-one first write PER
            // CLUSTER (Orleans single activation per tag); cross-cluster uniqueness remains the storage layer's job
            // (G15/G16 conditional unique-append). Conflicts surface through the EXISTING ResultBox.Error channel — no new
            // public exception type is added.
            var expectedVersion = string.IsNullOrEmpty(lastSortableUniqueId) ? string.Empty : lastSortableUniqueId;
            var currentVersion = string.IsNullOrEmpty(_latestSortableUniqueId) ? string.Empty : _latestSortableUniqueId;

            // SEK-G22: a different cluster may have committed after this actor successfully cached an empty tag. The
            // command-side fold then carries a real non-empty expected version while this activation still sees empty.
            // Re-read authoritatively only for that one anomalous shape. This helper assumes _reservationLock is held: it
            // must never enter the ordinary catch-up path, which would try to acquire the same lock again. A successful
            // read is reconciled into the cache before the exact-match decision, including a conflicting other version,
            // so a later expect-empty reservation cannot reopen G19's first-write hole.
            if (expectedVersion.Length > 0 && currentVersion.Length == 0)
            {
                var refreshResult = await RefreshLatestTagUnderReservationLockAsync();
                if (refreshResult.IsSuccess)
                {
                    currentVersion = string.IsNullOrEmpty(_latestSortableUniqueId)
                        ? string.Empty
                        : _latestSortableUniqueId;
                }
                // A failed read changes no cache state. The unchanged empty current then reaches the ordinary exact-match
                // conflict below, preserving the existing public failure channel while still failing closed.
            }

            if (!string.Equals(expectedVersion, currentVersion, StringComparison.Ordinal))
            {
                return ResultBox.Error<TagWriteReservation>(
                    new Exception(
                        $"Tag {await GetTagActorIdAsync()} has been modified. Expected version: {lastSortableUniqueId}, Current version: {_latestSortableUniqueId}"));
            }

            // On a matching non-empty expectation the current version is already equal (no-op); an empty expectation leaves
            // the empty current untouched. The authoritative version advance happens on the durable commit, not here.
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

    /// <summary>
    ///     Performs the bounded SEK-G22 authoritative re-check while <see cref="_reservationLock" /> is already held.
    ///     This method deliberately does not call the catch-up path or acquire the reservation lock.
    /// </summary>
    private async Task<ResultBox<string>> RefreshLatestTagUnderReservationLockAsync()
    {
        if (_eventStore == null)
        {
            return ResultBox.Error<string>(
                new InvalidOperationException($"Cannot authoritatively refresh tag {_tagName} without an event store"));
        }

        try
        {
            var tag = _tagTypes.GetTag(_tagName);
            var latestTagResult = await _eventStore.GetLatestTagAsync(tag);
            if (!latestTagResult.IsSuccess)
            {
                return ResultBox.Error<string>(latestTagResult.GetException());
            }

            var authoritativeVersion = latestTagResult.GetValue().LastSortedUniqueId ?? string.Empty;

            // The reservation lock makes the normal entry condition (cached empty) stable across the await. Keep this
            // monotonic guard as part of the primitive's contract so a future caller can never replace a newer non-empty
            // cache value with an older/empty store observation.
            if (string.IsNullOrEmpty(_latestSortableUniqueId) ||
                (!string.IsNullOrEmpty(authoritativeVersion) &&
                    string.Compare(authoritativeVersion, _latestSortableUniqueId, StringComparison.Ordinal) > 0))
            {
                _latestSortableUniqueId = authoritativeVersion;
            }

            return ResultBox.FromValue(_latestSortableUniqueId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GeneralTagConsistentActor] Error during authoritative reservation refresh for tag {TagName}", _tagName);
            return ResultBox.Error<string>(ex);
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

    public Task NotifyEventWrittenAsync()
    {
        // Simply mark catch-up as incomplete to force refresh on next access
        _catchUpCompleted = false;
        return Task.CompletedTask;
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
            // Parse tag name using ITagTypes instead of GenericTag
            var tag = _tagTypes.GetTag(_tagName);

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
        catch (Exception ex)
        {
            // Catch-up failure must not block the reservation system, but we log the error for observability
            _logger.LogError(ex, "[GeneralTagConsistentActor] Error during catch-up for tag {TagName}", _tagName);
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
