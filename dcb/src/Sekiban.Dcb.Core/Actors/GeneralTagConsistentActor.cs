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
    private long _catchUpGeneration = -1;
    // Both generations start at zero: fresh activation relies on catch-up, whose errors remain swallowed.
    private long _invalidationGeneration;
    private long _validatedGeneration; // Written only under _reservationLock.
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

    public async Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string? lastSortableUniqueId)
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

            // SEK-G30: null means the command never observed this tag. Keep the complete reservation lifecycle, but do
            // not compare, refresh, or adopt a version. Empty remains G19 AssertEmpty and non-empty remains ExactMatch.
            if (lastSortableUniqueId is null)
            {
                return ResultBox.FromValue(await CreateReservationAsync());
            }

            // SEK-G19: EXACT-MATCH optimistic-concurrency check after the SEK-G30 null branch, in-lock and post-catch-up. An
            // empty caller version means "I expect this tag to be EMPTY" (a first write) — NOT "skip the check". Comparing
            // empty expected/current values normalized to "" covers all five
            // classes: empty/empty pass (first write on an empty tag); empty/non-empty CONFLICT (a second first-write against
            // a tag that already has committed state — the #1085 hole); non-empty/empty CONFLICT (an update expecting a
            // version the tag never had — the secondary hole); non-empty mismatch CONFLICT; non-empty match pass. The
            // active-reservation rejection above is unchanged. GUARANTEE BOUNDARY: this holds at-most-one first write PER
            // CLUSTER (Orleans single activation per tag); cross-cluster uniqueness remains the storage layer's job
            // (G15/G16 conditional unique-append). Conflicts surface through the EXISTING ResultBox.Error channel — no new
            // public exception type is added.
            var expectedVersion = string.IsNullOrEmpty(lastSortableUniqueId) ? string.Empty : lastSortableUniqueId;
            var currentVersion = string.IsNullOrEmpty(_latestSortableUniqueId) ? string.Empty : _latestSortableUniqueId;

            // One generation-validated read serves both invalidation and SEK-G22's stale-empty reconciliation.
            if (_eventStore != null &&
                (Volatile.Read(ref _invalidationGeneration) != _validatedGeneration ||
                    (expectedVersion.Length > 0 && currentVersion.Length == 0)))
            {
                var refreshResult = await ReadValidatedUnderLockAsync();
                if (!refreshResult.IsSuccess)
                {
                    return ResultBox.Error<TagWriteReservation>(refreshResult.GetException());
                }
                currentVersion = _latestSortableUniqueId;
            }

            // Store-side conditional append (G15/G16) is still required for the reservation-expiry window and
            // writes on non-consistency tags whose notification arrives after an accepted refresh.

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
                PublishLatestUnderReservationLock(lastSortableUniqueId);
            }

            return ResultBox.FromValue(await CreateReservationAsync());
        }
        finally
        {
            _reservationLock.Release();
        }
    }

    private async Task<TagWriteReservation> CreateReservationAsync()
    {
        var reservationCode = Guid.NewGuid().ToString();
        var expiredUtc = DateTime.UtcNow.AddSeconds(_options.CancellationWindowSeconds);
        var reservation = new TagWriteReservation(
            reservationCode,
            expiredUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
            await GetTagActorIdAsync());
        _activeReservations[reservationCode] = reservation;
        return reservation;
    }

    /// <summary>
    ///     Performs an authoritative read for invalidation or SEK-G22 reconciliation while <see cref="_reservationLock" /> is already held.
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

            // Refresh can reconcile empty or non-empty caches; never lower a newer cached observation.
            PublishLatestUnderReservationLock(authoritativeVersion);

            return ResultBox.FromValue(_latestSortableUniqueId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GeneralTagConsistentActor] Error during authoritative reservation refresh for tag {TagName}", _tagName);
            return ResultBox.Error<string>(ex);
        }
    }

    // All callers hold _reservationLock, including catch-up publication.
    private void PublishLatestUnderReservationLock(string? version)
    {
        version ??= string.Empty;
        if (string.Compare(version, _latestSortableUniqueId, StringComparison.Ordinal) > 0)
        {
            _latestSortableUniqueId = version;
        }
    }

    private async Task<ResultBox<string>> ReadValidatedUnderLockAsync()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var generation = Volatile.Read(ref _invalidationGeneration);
            var result = await RefreshLatestTagUnderReservationLockAsync();
            if (!result.IsSuccess)
            {
                return ResultBox.Error<string>(new Exception(
                    $"Tag {_tagName} has been modified or could not be refreshed authoritatively",
                    result.GetException()));
            }
            if (Volatile.Read(ref _invalidationGeneration) == generation)
            {
                _validatedGeneration = generation;
                return ResultBox.FromValue(_latestSortableUniqueId);
            }
        }
        return ResultBox.Error<string>(new Exception($"Tag {_tagName} is being written concurrently; retry"));
    }

    private bool IsCatchUpComplete =>
        Volatile.Read(ref _catchUpGeneration) == Volatile.Read(ref _invalidationGeneration);

    private void RaiseCatchUpGeneration(long generation)
    {
        var current = Volatile.Read(ref _catchUpGeneration);
        while (current < generation)
        {
            var observed = Interlocked.CompareExchange(ref _catchUpGeneration, generation, current);
            if (observed == current) return;
            current = observed;
        }
    }

    public async Task<bool> ConfirmReservationAsync(TagWriteReservation reservation)
    {
        Interlocked.Increment(ref _invalidationGeneration);
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
        Interlocked.Increment(ref _invalidationGeneration);
        return Task.CompletedTask;
    }

    private async Task EnsureCatchUpCompletedAsync()
    {
        var generation = Volatile.Read(ref _invalidationGeneration);
        if (IsCatchUpComplete)
        {
            return;
        }

        await _catchUpLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (IsCatchUpComplete)
            {
                return;
            }

            if (_eventStore == null)
            {
                RaiseCatchUpGeneration(generation);
                return;
            }

            // Catch up from event store
            await CatchUpFromEventStoreAsync();
        }
        finally
        {
            _catchUpLock.Release();
        }
    }

    private async Task CatchUpFromEventStoreAsync()
    {
        var generation = Volatile.Read(ref _invalidationGeneration);
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
                    PublishLatestUnderReservationLock(tagState.LastSortedUniqueId);
                    RaiseCatchUpGeneration(generation);
                }
                finally
                {
                    _reservationLock.Release();
                }
            }
            // Failed reads publish no completion, so the next call retries (one catch-up read per failing call).
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
