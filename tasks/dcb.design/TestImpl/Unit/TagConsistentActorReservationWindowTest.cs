using DcbLib.Actors;
using DcbLib.Common;
using DcbLib.InMemory;
using Xunit;

namespace Unit;

/// <summary>
/// Tests for TagConsistentActor reservation window behavior
/// </summary>
public class TagConsistentActorReservationWindowTest
{
    [Fact]
    public async Task TagConsistentActor_Should_Block_Reservations_During_Window_And_Allow_After_Expiration()
    {
        // Arrange
        var tagName = "TestTag:123";
        var options = new TagConsistentActorOptions
        {
            CancellationWindowSeconds = 2.0 // Short window for testing
        };
        var actor = new InMemoryTagConsistentActor(tagName, null, options);
        
        // Act & Assert
        
        // Step 1: Make first reservation
        var firstReservationResult = await actor.MakeReservationAsync("unique-id-1");
        Assert.True(firstReservationResult.IsSuccess);
        var firstReservation = firstReservationResult.GetValue();
        
        // Step 2: Try to make another reservation while first is active (should fail)
        var secondReservationResult = await actor.MakeReservationAsync("unique-id-2");
        Assert.False(secondReservationResult.IsSuccess);
        
        // Step 3: Wait for the reservation window to expire
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        
        // Step 4: Now we should be able to make a new reservation
        var thirdReservationResult = await actor.MakeReservationAsync("unique-id-3");
        Assert.True(thirdReservationResult.IsSuccess);
        var thirdReservation = thirdReservationResult.GetValue();
        
        // Step 5: Try to confirm the original (expired) reservation (should fail)
        var confirmResult = await actor.ConfirmReservationAsync(firstReservation);
        Assert.False(confirmResult);
        
        // Step 6: Confirm the new reservation (should succeed)
        var confirmNewResult = await actor.ConfirmReservationAsync(thirdReservation);
        Assert.True(confirmNewResult);
        
        // Verify the latest sortable unique ID was updated to the third one
        var latestId = await actor.GetLatestSortableUniqueIdAsync();
        Assert.Equal("unique-id-3", latestId);
    }
    
    [Fact]
    public async Task TagConsistentActor_Should_Allow_Reservation_After_Confirmation()
    {
        // Arrange
        var tagName = "TestTag:456";
        var actor = new InMemoryTagConsistentActor(tagName);
        
        // Act & Assert
        
        // Make and confirm first reservation
        var firstReservationResult = await actor.MakeReservationAsync("unique-id-1");
        Assert.True(firstReservationResult.IsSuccess);
        var firstReservation = firstReservationResult.GetValue();
        
        var confirmResult = await actor.ConfirmReservationAsync(firstReservation);
        Assert.True(confirmResult);
        
        // Should be able to make new reservation immediately after confirmation
        var secondReservationResult = await actor.MakeReservationAsync("unique-id-2");
        Assert.True(secondReservationResult.IsSuccess);
    }
    
    [Fact]
    public async Task TagConsistentActor_Should_Allow_Reservation_After_Cancellation()
    {
        // Arrange
        var tagName = "TestTag:789";
        var actor = new InMemoryTagConsistentActor(tagName);
        
        // Act & Assert
        
        // Make and cancel first reservation
        var firstReservationResult = await actor.MakeReservationAsync("unique-id-1");
        Assert.True(firstReservationResult.IsSuccess);
        var firstReservation = firstReservationResult.GetValue();
        
        var cancelResult = await actor.CancelReservationAsync(firstReservation);
        Assert.True(cancelResult);
        
        // Should be able to make new reservation immediately after cancellation
        var secondReservationResult = await actor.MakeReservationAsync("unique-id-2");
        Assert.True(secondReservationResult.IsSuccess);
    }
    
    [Fact]
    public async Task TagConsistentActor_Should_Track_Multiple_Reservation_Expirations()
    {
        // Arrange
        var tagName = "TestTag:MultiExpire";
        var options = new TagConsistentActorOptions
        {
            CancellationWindowSeconds = 1.0 // Very short window
        };
        var actor = new InMemoryTagConsistentActor(tagName, null, options);
        
        // Make reservation
        var reservationResult = await actor.MakeReservationAsync("unique-id-1");
        Assert.True(reservationResult.IsSuccess);
        
        // Check active reservations before expiration
        var activeBeforeExpiration = await actor.GetActiveReservationsAsync();
        Assert.Single(activeBeforeExpiration);
        
        // Wait for expiration
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        
        // Check active reservations after expiration (cleanup should happen)
        var activeAfterExpiration = await actor.GetActiveReservationsAsync();
        Assert.Empty(activeAfterExpiration);
    }
}