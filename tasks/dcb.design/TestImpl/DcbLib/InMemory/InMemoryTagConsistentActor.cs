using System.Collections.Concurrent;
using DcbLib.Actors;
using DcbLib.Storage;
using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.InMemory;

/// <summary>
/// In-memory implementation of ITagConsistentActorCommon for testing
/// Manages tag write reservations to ensure consistency
/// Supports lazy initialization by catching up from event store
/// </summary>
public class InMemoryTagConsistentActor : ITagConsistentActorCommon
{
    private readonly string _tagName;
    private readonly IEventStore? _eventStore;
    private readonly ConcurrentDictionary<string, TagWriteReservation> _activeReservations = new();
    private readonly SemaphoreSlim _reservationLock = new(1, 1);
    private readonly SemaphoreSlim _catchUpLock = new(1, 1);
    private string _latestSortableUniqueId = "";
    private volatile bool _catchUpCompleted = false;
    
    public InMemoryTagConsistentActor(string tagName)
        : this(tagName, null)
    {
    }
    
    public InMemoryTagConsistentActor(string tagName, IEventStore? eventStore)
    {
        _tagName = tagName ?? throw new ArgumentNullException(nameof(tagName));
        _eventStore = eventStore;
    }
    
    public Task<string> GetTagActorIdAsync()
    {
        return Task.FromResult(_tagName);
    }
    
    public async Task<string> GetLatestSortableUniqueIdAsync()
    {
        // Ensure catch-up is completed before acquiring lock
        await EnsureCatchUpCompletedAsync();
        
        await _reservationLock.WaitAsync();
        try
        {
            return _latestSortableUniqueId;
        }
        finally
        {
            _reservationLock.Release();
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
            
            // Update the latest sortable unique ID
            if (!string.IsNullOrEmpty(lastSortableUniqueId))
            {
                _latestSortableUniqueId = lastSortableUniqueId;
            }
            
            // Create new reservation
            var reservationCode = Guid.NewGuid().ToString();
            var expiredUtc = DateTime.UtcNow.AddSeconds(30); // 30 second timeout
            var reservation = new TagWriteReservation(
                reservationCode,
                expiredUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
                await GetTagActorIdAsync()
            );
            
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
                return existingReservation.Equals(reservation);
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
            // Parse tag name to create tag
            var tagParts = _tagName.Split(':');
            if (tagParts.Length < 2)
            {
                // Invalid tag format, mark as caught up
                return;
            }
            
            var tag = new GenericTag(tagParts[0], tagParts[1]);
            
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
            else
            {
                // No tag exists yet or error reading, which is fine
                // The actor starts with empty state
            }
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
            .Where(kvp => DateTime.Parse(kvp.Value.ExpiredUTC, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal) < now)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var code in expiredCodes)
        {
            _activeReservations.TryRemove(code, out _);
        }
    }
    
    /// <summary>
    /// Gets the current active reservations (for testing purposes)
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
    
    /// <summary>
    /// Generic tag implementation for use within the actor
    /// </summary>
    private class GenericTag : ITag
    {
        private readonly string _tagGroup;
        private readonly string _tagContent;
        
        public GenericTag(string tagGroup, string tagContent)
        {
            _tagGroup = tagGroup;
            _tagContent = tagContent;
        }
        
        public bool IsConsistencyTag() => true;
        public string GetTagGroup() => _tagGroup;
        public string GetTag() => $"{_tagGroup}:{_tagContent}";
    }
}