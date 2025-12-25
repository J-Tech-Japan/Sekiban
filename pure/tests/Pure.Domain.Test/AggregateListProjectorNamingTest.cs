using Sekiban.Pure.Projectors;
namespace Pure.Domain.Test;

public class AggregateListProjectorNamingTest
{
    [Fact]
    public void AggregateListProjector_GetMultiProjectorName_ReturnsLowercaseHyphenatedName()
    {
        // Test with UserProjector
        var userProjectorName = AggregateListProjector<UserProjector>.GetMultiProjectorName();
        Assert.Equal("alp-userprojector", userProjectorName);  // Using "alp" abbreviation for AggregateListProjector
        Assert.Equal(userProjectorName.ToLowerInvariant(), userProjectorName);
        Assert.DoesNotContain("`", userProjectorName);
        Assert.DoesNotContain("_", userProjectorName);

        // Test with ShoppingCartProjector
        var cartProjectorName = AggregateListProjector<ShoppingCartProjector>.GetMultiProjectorName();
        Assert.Equal("alp-shoppingcartprojector", cartProjectorName);  // Using "alp" abbreviation
        Assert.Equal(cartProjectorName.ToLowerInvariant(), cartProjectorName);
    }

    [Fact]
    public void AggregateListProjector_Names_AreKubernetesCompliant()
    {
        var names = new[]
        {
            AggregateListProjector<UserProjector>.GetMultiProjectorName(),
            AggregateListProjector<ShoppingCartProjector>.GetMultiProjectorName()
        };

        foreach (var name in names)
        {
            // Check Kubernetes naming requirements
            Assert.True(name.Length <= 63);
            Assert.Matches(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", name);
        }
    }
}
