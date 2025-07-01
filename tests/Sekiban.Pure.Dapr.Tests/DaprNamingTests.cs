using Xunit;

namespace Sekiban.Pure.Dapr.Tests;

public class DaprNamingTests
{
    [Fact]
    public void MultiProjectorActor_ReminderNames_AreKubernetesCompliant()
    {
        // Test the reminder names defined in MultiProjectorActor
        var reminderNames = new[] { "snapshot_reminder", "event_check_reminder" };
        
        foreach (var name in reminderNames)
        {
            Assert.True(IsValidKubernetesName(name), 
                $"Reminder name '{name}' should be Kubernetes-compliant");
        }
    }
    
    [Theory]
    [InlineData("aggregatelistprojector-userprojector")]
    [InlineData("aggregatelistprojector-orderprojector")]
    [InlineData("aggregatelistprojector-shoppingcartprojector")]
    public void NewProjectorNamingPattern_IsKubernetesCompliant(string projectorName)
    {
        Assert.True(IsValidKubernetesName(projectorName),
            $"Projector name '{projectorName}' should be Kubernetes-compliant");
    }
    
    [Fact]
    public void SimulatedDaprJobName_IsKubernetesCompliant()
    {
        // Simulate how Dapr creates job names for reminders
        var actorType = "multiprojectoractor"; // lowercase
        var actorId = "aggregatelistprojector-userprojector"; // our new naming pattern
        var reminderName = "snapshot-reminder"; // hyphenated version
        var partition = "default";
        
        // Simulate Dapr's job naming (simplified - actual format may vary)
        var jobName = $"actorreminder-{partition}-{actorType}-{actorId}-{reminderName}";
        
        // Verify the job name is valid
        Assert.True(jobName.Length <= 63, 
            $"Job name length ({jobName.Length}) should not exceed 63 characters");
        Assert.True(IsValidKubernetesName(jobName),
            $"Job name '{jobName}' should be Kubernetes-compliant");
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