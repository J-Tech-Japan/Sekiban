using System.Collections.Concurrent;
using DcbLib.Actors;
using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.InMemory;

/// <summary>
/// In-memory implementation of ITagConsistentActorCommon for testing
/// Manages tag write reservations to ensure consistency
/// </summary>
public class InMemoryTagConsistentActor : ITagConsistentActorCommon
{
    private readonly string _tagName;
    private readonly ConcurrentDictionary<string, TagWriteReservation> _activeReservations = new();
    private readonly object _reservationLock = new();
    
    public InMemoryTagConsistentActor(string tagName)
    {
        _tagName = tagName ?? throw new ArgumentNullException(nameof(tagName));
    }
    
    public string GetTagActorId()
    {
        return _tagName;
    }
    
    public ResultBox<TagWriteReservation> MakeReservation(string lastSortableUniqueId)
    {
        lock (_reservationLock)
        {
            // Clean up expired reservations
            CleanupExpiredReservations();
            
            // Check if there are any active reservations
            if (_activeReservations.Any())
            {
                return ResultBox.Error<TagWriteReservation>(
                    new Exception($"Tag {GetTagActorId()} is currently reserved"));
            }
            
            // Create new reservation
            var reservationCode = Guid.NewGuid().ToString();
            var expiredUtc = DateTime.UtcNow.AddSeconds(30); // 30 second timeout
            var reservation = new TagWriteReservation(
                reservationCode,
                expiredUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
                GetTagActorId()
            );
            
            _activeReservations[reservationCode] = reservation;
            
            return ResultBox.FromValue(reservation);
        }
    }
    
    public bool ConfirmReservation(TagWriteReservation reservation)
    {
        if (reservation == null) return false;
        
        lock (_reservationLock)
        {
            if (_activeReservations.TryRemove(reservation.ReservationCode, out var existingReservation))
            {
                // Verify it's the same reservation
                return existingReservation.Equals(reservation);
            }
            
            return false;
        }
    }
    
    public bool CancelReservation(TagWriteReservation reservation)
    {
        if (reservation == null) return false;
        
        lock (_reservationLock)
        {
            return _activeReservations.TryRemove(reservation.ReservationCode, out _);
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
    public IEnumerable<TagWriteReservation> GetActiveReservations()
    {
        lock (_reservationLock)
        {
            CleanupExpiredReservations();
            return _activeReservations.Values.ToList();
        }
    }
}