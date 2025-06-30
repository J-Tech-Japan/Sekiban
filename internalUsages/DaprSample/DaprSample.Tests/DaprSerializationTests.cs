using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using DaprSample.Domain.User.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Dapr.Serialization;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace DaprSample.Tests;

public class DaprSerializationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ServiceProvider? _serviceProvider;
    private ISekibanExecutor? _executor;
    private SekibanDomainTypes? _domainTypes;

    public DaprSerializationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        
        // Configure logging to test output
        services.AddLogging(builder => 
        {
            builder.AddXunit(_output);
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Add required services for testing
        services.AddMemoryCache();
        
        // Generate domain types
        _domainTypes = DaprSample.Domain.Generated.DaprSampleDomainDomainTypes.Generate(
            DaprSample.Domain.DaprSampleEventsJsonContext.Default.Options);
        services.AddSingleton(_domainTypes);

        _serviceProvider = services.BuildServiceProvider();
        
        // Create in-memory executor for testing
        var repository = new Repository();
        var metadataProvider = new FunctionCommandMetadataProvider(() => "test");
        _executor = new InMemorySekibanExecutor(_domainTypes, metadataProvider, repository, _serviceProvider);
        
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _serviceProvider?.Dispose();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task DaprActorSerialization_CommandResponse_ShouldWorkEndToEnd()
    {
        // Arrange - Create a user to get a real CommandResponse
        var userId = Guid.NewGuid();
        var createCommand = new CreateUser(userId, "Test User", "test@example.com");
        var createResult = await _executor!.CommandAsync(createCommand);
        Assert.True(createResult.IsSuccess);
        
        var originalResponse = createResult.GetValue();
        
        // Act - Simulate what happens in Dapr actor communication
        // 1. Convert to SerializableCommandResponse
        var serializableResponse = await SerializableCommandResponse.CreateFromAsync(
            originalResponse, 
            DaprSerializationOptions.Default.JsonSerializerOptions);
        
        // 2. Serialize to JSON string (what the actor returns)
        var jsonString = JsonSerializer.Serialize(
            serializableResponse, 
            DaprSerializationOptions.Default.JsonSerializerOptions);
        
        _output.WriteLine($"Serialized JSON: {jsonString}");
        
        // 3. Deserialize from JSON string (what the executor receives)
        var deserializedResponse = JsonSerializer.Deserialize<SerializableCommandResponse>(
            jsonString, 
            DaprSerializationOptions.Default.JsonSerializerOptions);
        
        Assert.NotNull(deserializedResponse);
        
        // 4. Convert back to CommandResponse
        var finalResult = await deserializedResponse!.ToCommandResponseAsync(_domainTypes!);
        
        // Assert
        if (!finalResult.HasValue)
        {
            _output.WriteLine($"Failed to convert back to CommandResponse");
            _output.WriteLine($"SerializableResponse has {deserializedResponse.Events.Count} events");
            foreach (var evt in deserializedResponse.Events)
            {
                _output.WriteLine($"Event: {evt.EventTypeName}, Version: {evt.Version}");
                // Try to look up the event type
                var eventType = _domainTypes!.EventTypes.GetEventTypeByName(evt.EventTypeName);
                if (eventType == null)
                {
                    _output.WriteLine($"  Event type '{evt.EventTypeName}' not found in domain types!");
                }
            }
            Assert.True(finalResult.HasValue, "Should successfully convert back to CommandResponse");
        }
        var finalResponse = finalResult.Value!;
        
        Assert.Equal(originalResponse.Version, finalResponse.Version);
        Assert.Equal(originalResponse.Events.Count, finalResponse.Events.Count);
        Assert.Equal(originalResponse.PartitionKeys.AggregateId, finalResponse.PartitionKeys.AggregateId);
        
        _output.WriteLine($"Success! Original version: {originalResponse.Version}, Final version: {finalResponse.Version}");
    }

    [Fact]
    public void DaprSerializationOptions_ShouldSerializeSerializableCommandResponse()
    {
        // Arrange
        var response = new SerializableCommandResponse
        {
            AggregateId = Guid.NewGuid(),
            Group = PartitionKeys.DefaultAggregateGroupName,
            RootPartitionKey = PartitionKeys.DefaultRootPartitionKey,
            Version = 1,
            Events = new List<SerializableCommandResponse.SerializableEvent>()
        };
        
        // Act - This should not throw
        var json = JsonSerializer.Serialize(response, DaprSerializationOptions.Default.JsonSerializerOptions);
        var deserialized = JsonSerializer.Deserialize<SerializableCommandResponse>(
            json, DaprSerializationOptions.Default.JsonSerializerOptions);
        
        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(response.AggregateId, deserialized!.AggregateId);
        Assert.Equal(response.Version, deserialized.Version);
        
        _output.WriteLine($"Successfully serialized and deserialized with DaprSerializationOptions");
    }
}