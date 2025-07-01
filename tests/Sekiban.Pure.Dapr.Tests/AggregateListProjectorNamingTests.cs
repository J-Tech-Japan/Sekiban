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
        Assert.Equal("aggregatelistprojector-testuserprojector", name);
        Assert.True(KubernetesNameHelper.IsValidKubernetesName(name), 
            $"Generated name '{name}' should be Kubernetes-compliant");
    }
    
    [Fact]
    public void AggregateListProjector_GeneratedActorId_IsKubernetesCompliant()
    {
        // This simulates how Dapr would create the full reminder job name
        var actorType = "MultiProjectorActor";
        var actorId = AggregateListProjector<TestUserProjector>.GetMultiProjectorName();
        var reminderName = "snapshot_reminder";
        var partition = "default";
        
        // Simulate Dapr's job naming pattern (simplified)
        var jobNameParts = new[] { "actorreminder", partition, actorType, actorId, reminderName };
        var simulatedJobName = string.Join("-", jobNameParts.Select(p => p.ToLowerInvariant().Replace("_", "-")));
        
        // Assert
        Assert.True(simulatedJobName.Length <= 63, 
            $"Job name length ({simulatedJobName.Length}) should not exceed 63 characters");
        Assert.True(simulatedJobName == simulatedJobName.ToLowerInvariant(), 
            "Job name should be all lowercase");
        Assert.DoesNotContain("`", simulatedJobName);
        Assert.DoesNotContain("_", simulatedJobName);
        Assert.Matches("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", simulatedJobName);
    }
}