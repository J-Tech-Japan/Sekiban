using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DaprSekiban.Domain.Aggregates.User;
using DaprSekiban.Domain.Aggregates.User.Commands;
using DaprSekiban.Domain.Aggregates.User.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Repositories;
using Sekiban.Pure.Command.Handlers;
using Xunit;
using Xunit.Abstractions;

namespace DaprSekiban.Unit;

public class SerializableCommandResponseTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ServiceProvider? _serviceProvider;
    private ISekibanExecutor? _executor;
    private SekibanDomainTypes? _domainTypes;

    public SerializableCommandResponseTests(ITestOutputHelper output)
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
        _domainTypes = SharedDomain.Generated.DaprSekibanDomainDomainTypes.Generate(
            SharedDomain.DaprSekibanDomainEventsJsonContext.Default.Options);
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
    public async Task SerializableCommandResponse_RoundTrip_ShouldPreserveData()
    {
        // Arrange - Create a user first to get a real CommandResponse
        var userId = Guid.NewGuid();
        var createCommand = new CreateUser(userId, "Test User", "test@example.com");
        var createResult = await _executor!.CommandAsync(createCommand);
        Assert.True(createResult.IsSuccess);
        
        var originalResponse = createResult.GetValue();
        _output.WriteLine($"Original Response - Version: {originalResponse.Version}, Events: {originalResponse.Events.Count}");
        
        // Act - Convert to SerializableCommandResponse and back
        var serializableResponse = await SerializableCommandResponse.CreateFromAsync(
            originalResponse, 
            Sekiban.Pure.Dapr.Serialization.DaprSerializationOptions.Default.JsonSerializerOptions);
        
        _output.WriteLine($"Serializable Response - Version: {serializableResponse.Version}, Events: {serializableResponse.Events.Count}");
        
        // Serialize and log for debugging
        var json = System.Text.Json.JsonSerializer.Serialize(serializableResponse, Sekiban.Pure.Dapr.Serialization.DaprSerializationOptions.Default.JsonSerializerOptions);
        _output.WriteLine($"Serialized JSON: {json}");
        
        // Deserialize back
        var deserializedSerializable = System.Text.Json.JsonSerializer.Deserialize<SerializableCommandResponse>(
            json, Sekiban.Pure.Dapr.Serialization.DaprSerializationOptions.Default.JsonSerializerOptions);
        
        Assert.NotNull(deserializedSerializable);
        
        var roundTripResult = await deserializedSerializable!.ToCommandResponseAsync(_domainTypes);
        
        // Assert
        if (!roundTripResult.HasValue)
        {
            _output.WriteLine("Failed to convert back to CommandResponse");
        }
        Assert.True(roundTripResult.HasValue, "Should successfully convert back to CommandResponse");
        
        var roundTripResponse = roundTripResult.Value!;
        Assert.Equal(originalResponse.Version, roundTripResponse.Version);
        Assert.Equal(originalResponse.Events.Count, roundTripResponse.Events.Count);
        Assert.Equal(originalResponse.PartitionKeys.AggregateId, roundTripResponse.PartitionKeys.AggregateId);
    }

    [Fact]
    public async Task SerializableCommandResponse_WithEmptyEvents_ShouldDeserialize()
    {
        // Arrange - Create a SerializableCommandResponse with empty events
        var response = new SerializableCommandResponse
        {
            AggregateId = Guid.NewGuid(),
            Group = PartitionKeys.DefaultAggregateGroupName,
            RootPartitionKey = PartitionKeys.DefaultRootPartitionKey,
            Version = 1,
            Events = new List<SerializableCommandResponse.SerializableEvent>()
        };
        
        // Act - Serialize and deserialize
        var json = System.Text.Json.JsonSerializer.Serialize(response, Sekiban.Pure.Dapr.Serialization.DaprSerializationOptions.Default.JsonSerializerOptions);
        _output.WriteLine($"Empty events JSON: {json}");
        
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<SerializableCommandResponse>(
            json, Sekiban.Pure.Dapr.Serialization.DaprSerializationOptions.Default.JsonSerializerOptions);
        
        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(response.AggregateId, deserialized!.AggregateId);
        Assert.Equal(response.Version, deserialized.Version);
        Assert.Empty(deserialized.Events);
        
        // Try to convert to CommandResponse
        var commandResponseResult = await deserialized.ToCommandResponseAsync(_domainTypes);
        Assert.True(commandResponseResult.HasValue);
        Assert.Empty(commandResponseResult.Value!.Events);
    }

    [Fact]
    public async Task SerializableEvent_WithActualEvent_ShouldSerializeCorrectly()
    {
        // Arrange - Create an actual event
        var userId = Guid.NewGuid();
        var partitionKeys = new PartitionKeys(userId, PartitionKeys.DefaultAggregateGroupName, PartitionKeys.DefaultRootPartitionKey);
        var userCreatedPayload = new UserCreated(userId, "Test User", "test@example.com");
        var userCreatedEvent = new Event<UserCreated>(
            Guid.NewGuid(),
            userCreatedPayload,
            partitionKeys,
            SortableUniqueIdValue.Generate(DateTime.UtcNow, Guid.NewGuid()),
            1,
            new EventMetadata(string.Empty, string.Empty, string.Empty)
        );
        
        // Act - Create SerializableEvent
        var serializableEvent = await SerializableCommandResponse.SerializableEvent.CreateFromAsync(
            userCreatedEvent, 
            _domainTypes!.JsonSerializerOptions);
        
        _output.WriteLine($"SerializableEvent - Type: {serializableEvent.EventTypeName}");
        _output.WriteLine($"SerializableEvent - Version: {serializableEvent.Version}");
        _output.WriteLine($"SerializableEvent - AggregateId: {serializableEvent.AggregateId}");
        
        // Check if compressed payload is created
        Assert.NotEmpty(serializableEvent.CompressedPayloadJson);
        
        // Try to convert back
        var eventResult = await serializableEvent.ToEventAsync(_domainTypes);
        
        // Assert
        Assert.True(eventResult.HasValue, "Should successfully convert back to IEvent");
        var reconstructedEvent = eventResult.Value!;
        Assert.Equal(userCreatedEvent.Version, reconstructedEvent.Version);
        Assert.Equal(userCreatedEvent.PartitionKeys.AggregateId, reconstructedEvent.PartitionKeys.AggregateId);
        
        var payload = reconstructedEvent.GetPayload() as UserCreated;
        Assert.NotNull(payload);
        Assert.Equal(userCreatedPayload.Name, payload!.Name);
        Assert.Equal(userCreatedPayload.Email, payload.Email);
    }
}