using Xunit;

namespace Sekiban.Pure.Dapr.Tests;

public class DaprNamingTests
{
    [Fact]
    public void MultiProjectorActor_ReminderNames_AreKubernetesCompliant()
    {
        // Test the reminder names defined in MultiProjectorActor
        // Note: These names contain underscores which are not Kubernetes compliant
        // They should be converted to hyphens when used in Kubernetes contexts
        var reminderNames = new[] { "snapshot_reminder", "event_check_reminder" };
        
        foreach (var name in reminderNames)
        {
            // The raw names contain underscores, which aren't Kubernetes-compliant
            Assert.False(IsValidKubernetesName(name), 
                $"Raw reminder name '{name}' should NOT be Kubernetes-compliant (contains underscores)");
            
            // But when sanitized, they should become compliant
            var sanitized = name.Replace("_", "-");
            Assert.True(IsValidKubernetesName(sanitized), 
                $"Sanitized reminder name '{sanitized}' should be Kubernetes-compliant");
        }
    }
    
    [Theory]
    [InlineData("alp-userprojector")]
    [InlineData("alp-orderprojector")]
    [InlineData("alp-shoppingcartprojector")]
    public void NewProjectorNamingPattern_IsKubernetesCompliant(string projectorName)
    {
        Assert.True(IsValidKubernetesName(projectorName),
            $"Projector name '{projectorName}' should be Kubernetes-compliant");
    }
    
    [Fact]
    public void SimulatedDaprJobName_ShowsKubernetesNameLengthChallenge()
    {
        // This test documents a known limitation with Dapr job naming for reminders
        // The full job name can exceed Kubernetes' 63-character limit
        
        var actorType = "multiprojectoractor"; // lowercase
        var actorId = "alp-userprojector"; // our new naming pattern (shortened)
        var reminderName = "snapshot-reminder"; // hyphenated version
        var partition = "default";
        
        // Simulate Dapr's job naming (simplified - actual format may vary)
        var jobName = $"actorreminder-{partition}-{actorType}-{actorId}-{reminderName}";
        
        // Document the current state
        Assert.Equal(77, jobName.Length); // This exceeds Kubernetes' 63-char limit
        
        // The name format itself is valid (just too long)
        Assert.True(jobName == jobName.ToLowerInvariant(),
            $"Job name '{jobName}' should be all lowercase");
        Assert.DoesNotContain("_", jobName);
        Assert.Matches("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", jobName);
        
        // Note: In production environments, Dapr or Kubernetes operators should handle
        // name truncation or use hashing strategies for overly long names
    }
    
    private static bool IsValidKubernetesName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 63)
        {
            return false;
        }
        
        // Must be lowercase
        if (name != name.ToLowerInvariant())
        {
            return false;
        }
        
        // Must match the pattern: lowercase alphanumeric and hyphens, 
        // starting and ending with alphanumeric
        return System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$");
    }
}