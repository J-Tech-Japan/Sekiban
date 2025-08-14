using Sekiban.Dcb.Actors;
using Dcb.Domain;
using System.Globalization;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for TagConsistentActorOptions
/// </summary>
public class TagConsistentActorOptionsTest
{
    [Fact]
    public void TagConsistentActorOptions_Should_Have_Default_CancellationWindowSeconds()
    {
        // Arrange & Act
        var options = new TagConsistentActorOptions();

        // Assert
        Assert.Equal(30.0, options.CancellationWindowSeconds);
    }

    [Fact]
    public async Task TagConsistentActor_Should_Use_Custom_CancellationWindowSeconds()
    {
        // Arrange
        var tagName = "TestTag:123";
        var customOptions = new TagConsistentActorOptions
        {
            CancellationWindowSeconds = 60.0 // 1 minute instead of default 30 seconds
        };

    var domainTypes = DomainType.GetDomainTypes();
    var actor = new GeneralTagConsistentActor(tagName, null, customOptions, domainTypes);

        // Act
        var reservationResult = await actor.MakeReservationAsync("test-sortable-id");

        // Assert
        Assert.True(reservationResult.IsSuccess);
        var reservation = reservationResult.GetValue();

        // Parse the expiration time
        var expirationTime = DateTime.Parse(
            reservation.ExpiredUTC,
            null,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        // The expiration should be approximately 60 seconds from now
        var expectedExpiration = DateTime.UtcNow.AddSeconds(60);
        var timeDifference = Math.Abs((expirationTime - expectedExpiration).TotalSeconds);

        // Allow for small timing differences (within 1 second)
        Assert.True(
            timeDifference < 1.0,
            $"Expected expiration around {expectedExpiration:O}, but got {expirationTime:O}");
    }

    [Fact]
    public async Task TagConsistentActor_Should_Use_Default_CancellationWindowSeconds()
    {
        // Arrange
        var tagName = "TestTag:456";
    var domainTypes = DomainType.GetDomainTypes();
    var actor = new GeneralTagConsistentActor(tagName, null, new TagConsistentActorOptions(), domainTypes); // Using default options

        // Act
        var reservationResult = await actor.MakeReservationAsync("test-sortable-id");

        // Assert
        Assert.True(reservationResult.IsSuccess);
        var reservation = reservationResult.GetValue();

        // Parse the expiration time
        var expirationTime = DateTime.Parse(
            reservation.ExpiredUTC,
            null,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        // The expiration should be approximately 30 seconds from now (default)
        var expectedExpiration = DateTime.UtcNow.AddSeconds(30);
        var timeDifference = Math.Abs((expirationTime - expectedExpiration).TotalSeconds);

        // Allow for small timing differences (within 1 second)
        Assert.True(
            timeDifference < 1.0,
            $"Expected expiration around {expectedExpiration:O}, but got {expirationTime:O}");
    }
}
