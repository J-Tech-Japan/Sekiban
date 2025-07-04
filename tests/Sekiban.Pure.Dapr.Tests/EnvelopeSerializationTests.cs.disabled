using Xunit;
using Sekiban.Pure.Dapr.Actors;
using DaprCommandResponse = Sekiban.Pure.Dapr.Actors.CommandResponse;
using Sekiban.Pure.Dapr.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;
using Sekiban.Pure.Documents;
using Google.Protobuf;

namespace Sekiban.Pure.Dapr.Tests;

/// <summary>
/// Tests for envelope-based communication and Protobuf serialization
/// </summary>
public class EnvelopeSerializationTests
{
    private readonly IEnvelopeProtobufService _envelopeService;
    private readonly IDaprProtobufSerializationService _protobufService;
    private readonly IDaprTypeRegistry _typeRegistry;

    public EnvelopeSerializationTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        
        _typeRegistry = new DaprTypeRegistry();
        
        var options = Options.Create(new DaprSerializationOptions
        {
            EnableCompression = true,
            EnableTypeAliases = true,
            CompressionThreshold = 1024
        });

        _protobufService = new DaprProtobufSerializationService(
            _typeRegistry,
            options,
            loggerFactory.CreateLogger<DaprProtobufSerializationService>());

        var typeMapper = new ProtobufTypeMapper(loggerFactory.CreateLogger<ProtobufTypeMapper>());
        
        _envelopeService = new EnvelopeProtobufService(
            _protobufService,
            typeMapper,
            loggerFactory.CreateLogger<EnvelopeProtobufService>());
    }

    [Fact]
    public async Task CommandEnvelope_SerializesToJson_Successfully()
    {
        // Arrange
        var envelope = new CommandEnvelope
        {
            CommandType = "TestCommand",
            CommandPayload = new byte[] { 1, 2, 3, 4, 5 },
            AggregateId = Guid.NewGuid().ToString(),
            PartitionId = Guid.NewGuid(),
            RootPartitionKey = "test-tenant",
            Metadata = new Dictionary<string, string> { ["key"] = "value" },
            CorrelationId = Guid.NewGuid().ToString()
        };

        // Act
        var json = JsonSerializer.Serialize(envelope);
        var deserialized = JsonSerializer.Deserialize<CommandEnvelope>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(envelope.CommandType, deserialized.CommandType);
        Assert.Equal(envelope.CommandPayload, deserialized.CommandPayload);
        Assert.Equal(envelope.AggregateId, deserialized.AggregateId);
        Assert.Equal(envelope.PartitionId, deserialized.PartitionId);
        Assert.Equal(envelope.RootPartitionKey, deserialized.RootPartitionKey);
        Assert.Equal(envelope.CorrelationId, deserialized.CorrelationId);
    }

    [Fact]
    public async Task EventEnvelope_SerializesToJson_Successfully()
    {
        // Arrange
        var envelope = new EventEnvelope
        {
            EventType = "TestEvent",
            EventPayload = new byte[] { 10, 20, 30, 40, 50 },
            AggregateId = Guid.NewGuid().ToString(),
            PartitionId = Guid.NewGuid(),
            RootPartitionKey = "test-tenant",
            Version = 1,
            SortableUniqueId = "2024-01-01T00:00:00.000Z_1",
            Timestamp = DateTime.UtcNow,
            Metadata = new Dictionary<string, string> { ["eventKey"] = "eventValue" },
            CorrelationId = Guid.NewGuid().ToString(),
            CausationId = Guid.NewGuid().ToString()
        };

        // Act
        var json = JsonSerializer.Serialize(envelope);
        var deserialized = JsonSerializer.Deserialize<EventEnvelope>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(envelope.EventType, deserialized.EventType);
        Assert.Equal(envelope.EventPayload, deserialized.EventPayload);
        Assert.Equal(envelope.AggregateId, deserialized.AggregateId);
        Assert.Equal(envelope.Version, deserialized.Version);
        Assert.Equal(envelope.SortableUniqueId, deserialized.SortableUniqueId);
    }

    [Fact]
    public void ProtobufHelper_RegisterAndResolveType_Works()
    {
        // Arrange & Act
        ProtobufHelper.RegisterType<ProtobufCommandEnvelope>();
        
        // Assert - this should not throw
        var bytes = new ProtobufCommandEnvelope
        {
            CommandType = "TestCommand",
            CommandJson = ByteString.CopyFromUtf8("{}"),
            IsCompressed = false
        }.ToByteArray();

        var deserialized = ProtobufHelper.Deserialize<ProtobufCommandEnvelope>(bytes);
        Assert.NotNull(deserialized);
        Assert.Equal("TestCommand", deserialized.CommandType);
    }

    [Fact]
    public async Task CommandResponse_Success_SerializesCorrectly()
    {
        // Arrange
        var response = DaprCommandResponse.Success(
            eventPayloads: new List<byte[]> { new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6 } },
            eventTypes: new List<string> { "Event1", "Event2" },
            aggregateVersion: 2,
            aggregateStatePayload: new byte[] { 7, 8, 9 },
            aggregateStateType: "TestAggregate",
            metadata: new Dictionary<string, string> { ["responseKey"] = "responseValue" });

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<DaprCommandResponse>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsSuccess);
        Assert.Equal(2, deserialized.EventPayloads.Count);
        Assert.Equal(2, deserialized.EventTypes.Count);
        Assert.Equal(2, deserialized.AggregateVersion);
        Assert.NotNull(deserialized.AggregateStatePayload);
        Assert.Equal("TestAggregate", deserialized.AggregateStateType);
    }

    [Fact]
    public async Task CommandResponse_Failure_SerializesCorrectly()
    {
        // Arrange
        var errorData = new
        {
            Message = "Command validation failed",
            Code = "VALIDATION_ERROR",
            Details = new[] { "Field X is required", "Field Y is invalid" }
        };
        
        var response = DaprCommandResponse.Failure(
            JsonSerializer.Serialize(errorData),
            new Dictionary<string, string> { ["errorKey"] = "errorValue" });

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<DaprCommandResponse>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.False(deserialized.IsSuccess);
        Assert.NotNull(deserialized.ErrorJson);
        Assert.Contains("VALIDATION_ERROR", deserialized.ErrorJson);
    }

    [Fact]
    public async Task EventHandlingResponse_Success_SerializesCorrectly()
    {
        // Arrange
        var response = EventHandlingResponse.Success("lastEvent_123");

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<EventHandlingResponse>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsSuccess);
        Assert.Equal("lastEvent_123", deserialized.LastProcessedEventId);
        Assert.Null(deserialized.ErrorMessage);
    }

    [Fact]
    public async Task EnvelopeList_SerializesAsJsonArray()
    {
        // Arrange
        var envelopes = new List<EventEnvelope>
        {
            new EventEnvelope { EventType = "Event1", Version = 1 },
            new EventEnvelope { EventType = "Event2", Version = 2 },
            new EventEnvelope { EventType = "Event3", Version = 3 }
        };

        // Act
        var json = JsonSerializer.Serialize(envelopes);
        var deserialized = JsonSerializer.Deserialize<List<EventEnvelope>>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.Count);
        Assert.Equal("Event1", deserialized[0].EventType);
        Assert.Equal("Event2", deserialized[1].EventType);
        Assert.Equal("Event3", deserialized[2].EventType);
    }
}

/// <summary>
/// Test command for serialization tests
/// </summary>
public record TestCommand(string Name, int Value) : ICommandWithHandlerSerializable
{
    public Delegate GetCommandSpecifier() => () => this;
    public Delegate GetPartitionKeysSpecifier() => () => new PartitionKeys(Guid.NewGuid(), string.Empty, "test");
}

/// <summary>
/// Test event for serialization tests
/// </summary>
public record TestEvent(string Description, DateTime Timestamp) : IEventPayload
{
    public static IEvent Create(string description) => new Event<TestEvent>(new TestEvent(description, DateTime.UtcNow));
}