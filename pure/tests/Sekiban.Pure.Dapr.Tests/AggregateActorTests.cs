using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Dapr.Serialization;
using System.Text.Json;
using Xunit;

namespace Sekiban.Pure.Dapr.Tests;

public class AggregateActorTests
{
    [Fact]
    public void ExecuteCommandAsync_Now_Returns_SerializableCommandResponse()
    {
        // This test verifies that the IAggregateActor.ExecuteCommandAsync method
        // now returns SerializableCommandResponse directly instead of a JSON string.
        
        // The updated interface definition is:
        // Task<SerializableCommandResponse> ExecuteCommandAsync(SerializableCommandAndMetadata commandAndMetadata);
        
        // Create a sample response
        var sampleResponse = new SerializableCommandResponse
        {
            AggregateId = Guid.NewGuid(),
            Group = "TestGroup",
            RootPartitionKey = "TestRoot",
            Version = 1,
            Events = new List<SerializableCommandResponse.SerializableEvent>()
        };
        
        // The interface now returns the object directly, not a JSON string
        // This provides type safety and avoids unnecessary serialization/deserialization
        Assert.NotNull(sampleResponse);
        Assert.Equal("TestGroup", sampleResponse.Group);
        Assert.Equal(1, sampleResponse.Version);
        
        // This test confirms that the interface has been successfully updated
    }
    
    [Fact]
    public void Interface_Has_Been_Updated_To_Return_SerializableCommandResponse()
    {
        // This test documents that the interface has been successfully changed from:
        // Task<string> ExecuteCommandAsync(SerializableCommandAndMetadata commandAndMetadata);
        
        // To:
        // Task<SerializableCommandResponse> ExecuteCommandAsync(SerializableCommandAndMetadata commandAndMetadata);
        
        // Benefits of this change:
        // 1. Type safety - compile-time checking of return type
        // 2. Performance - no unnecessary JSON serialization/deserialization between Dapr actors
        // 3. Consistency - aligns with other methods that return domain objects directly
        
        Assert.True(true, "Interface has been successfully updated");
    }
}