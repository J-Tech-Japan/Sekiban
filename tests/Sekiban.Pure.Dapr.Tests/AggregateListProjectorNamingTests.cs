using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Dapr.Utils;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using Xunit;

namespace Sekiban.Pure.Dapr.Tests;

public class AggregateListProjectorNamingTests
{
    // Test aggregate payload
    public record TestUserAggregate : IAggregatePayload
    {
        public static TestUserAggregate Empty => new();
    }
    
    // Test projector for testing
    public class TestUserProjector : IAggregateProjector
    {
        public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
        {
            return payload;
        }
    }
    
    [Fact]
    public void AggregateListProjector_GetMultiProjectorName_ReturnsKubernetesCompliantName()
    {
        // Act
        var name = AggregateListProjector<TestUserProjector>.GetMultiProjectorName();
        
        // Assert
        Assert.Equal("alp-testuserprojector", name);
        Assert.True(KubernetesNameHelper.IsValidKubernetesName(name), 
            $"Generated name '{name}' should be Kubernetes-compliant");
    }
    
    [Fact]
    public void AggregateListProjector_GeneratedActorId_ShowsKubernetesNameLengthChallenge()
    {
        // This test documents a known limitation: when Dapr creates the full reminder job name,
        // it can exceed the Kubernetes 63-character limit for longer projector names.
        // The shortened "alp-" prefix helps but may not be sufficient for all cases.
        
        var actorType = "MultiProjectorActor";
        var actorId = AggregateListProjector<TestUserProjector>.GetMultiProjectorName();
        var reminderName = "snapshot_reminder";
        var partition = "default";
        
        // Simulate Dapr's job naming pattern (simplified)
        var jobNameParts = new[] { "actorreminder", partition, actorType, actorId, reminderName };
        var simulatedJobName = string.Join("-", jobNameParts.Select(p => p.ToLowerInvariant().Replace("_", "-")));
        
        // Document the current state
        Assert.Equal("alp-testuserprojector", actorId);
        Assert.Equal(81, simulatedJobName.Length); // This exceeds Kubernetes' 63-char limit
        
        // The name itself is valid Kubernetes format (just too long)
        Assert.True(simulatedJobName == simulatedJobName.ToLowerInvariant(), 
            "Job name should be all lowercase");
        Assert.DoesNotContain("`", simulatedJobName);
        Assert.DoesNotContain("_", simulatedJobName);
        Assert.Matches("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", simulatedJobName);
        
        // Note: In production, Dapr or the orchestrator should handle name truncation
        // or use hashing for overly long names. This is a platform concern, not application logic.
    }
}