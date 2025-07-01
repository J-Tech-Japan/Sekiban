using Sekiban.Pure.Dapr.Utils;
using Xunit;

namespace Sekiban.Pure.Dapr.Tests.Utils;

public class KubernetesNameHelperTests
{
    [Theory]
    [InlineData("AggregateListProjector`1UserProjector", "aggregatelistprojector-1userprojector")]
    [InlineData("MultiProjectorActor", "multiprojectoractor")]
    [InlineData("Test_Name-With.Special@Characters!", "test-name-with-special-characters")]
    [InlineData("UPPERCASE", "uppercase")]
    [InlineData("123StartWithNumber", "123startwithhnumber")]
    [InlineData("-start-with-hyphen", "start-with-hyphen")]
    [InlineData("end-with-hyphen-", "end-with-hyphen")]
    [InlineData("multiple---hyphens", "multiple-hyphens")]
    [InlineData("", "default")]
    [InlineData("   ", "default")]
    public void SanitizeForKubernetes_ProducesValidNames(string input, string expected)
    {
        // Act
        var result = KubernetesNameHelper.SanitizeForKubernetes(input);
        
        // Assert
        Assert.Equal(expected, result);
        Assert.True(KubernetesNameHelper.IsValidKubernetesName(result), 
            $"Sanitized name '{result}' should be valid");
    }
    
    [Fact]
    public void SanitizeForKubernetes_TruncatesLongNames()
    {
        // Arrange
        var longName = new string('a', 100);
        
        // Act
        var result = KubernetesNameHelper.SanitizeForKubernetes(longName);
        
        // Assert
        Assert.Equal(63, result.Length);
        Assert.True(KubernetesNameHelper.IsValidKubernetesName(result));
    }
    
    [Theory]
    [InlineData("valid-name", true)]
    [InlineData("another-valid-name123", true)]
    [InlineData("123valid", true)]
    [InlineData("UPPERCASE", false)]
    [InlineData("invalid_underscore", false)]
    [InlineData("invalid.dot", false)]
    [InlineData("-invalid-start", false)]
    [InlineData("invalid-end-", false)]
    [InlineData("", false)]
    [InlineData("a", true)]
    [InlineData("1", true)]
    public void IsValidKubernetesName_ValidatesCorrectly(string name, bool expectedValid)
    {
        // Act
        var result = KubernetesNameHelper.IsValidKubernetesName(name);
        
        // Assert
        Assert.Equal(expectedValid, result);
    }
    
    [Fact]
    public void AggregateListProjector_GeneratesValidKubernetesName()
    {
        // This test verifies that our new naming pattern for AggregateListProjector
        // produces valid Kubernetes names
        
        // Arrange
        var projectorNames = new[]
        {
            "aggregatelistprojector-userprojector",
            "aggregatelistprojector-orderprojector",
            "aggregatelistprojector-shoppingcartprojector"
        };
        
        // Assert
        foreach (var name in projectorNames)
        {
            Assert.True(KubernetesNameHelper.IsValidKubernetesName(name),
                $"Projector name '{name}' should be a valid Kubernetes name");
        }
    }
}