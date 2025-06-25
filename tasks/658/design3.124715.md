# Dapr Event Sourcing Serialization Design
**LLM Model: Claude Opus 4 (claude-opus-4-20250514)**

## Overview

This document outlines the design for implementing Dapr-based event sourcing in Sekiban with a focus on serialization strategies to handle interface type serialization challenges in Dapr actors.

## Problem Statement

Dapr actors have difficulty serializing interface types directly. The current implementation at `src/Sekiban.Pure.Dapr/Actors/IAggregateActor.cs:31` faces serialization issues when passing interface-based types as actor inputs.

## Proposed Architecture

### 1. Data Flow Overview

```
User Input (C# Command) 
    → SekibanDaprExecutor (accepts protobuf)
    → JSON-packed protobuf envelope
    → AggregateActor
    → Unpacked to C# Command
    → Generate C# Events
    → Pack as protobuf
    → AggregateEventHandlerActor
```

### 2. Component Design

#### 2.1 SekibanDaprExecutor

**Purpose**: Entry point for command execution that handles conversion between developer-friendly interfaces and Dapr-compatible formats.

**Key Responsibilities**:
- Accept commands in protobuf format
- Convert commands to JSON-packed envelope format
- Invoke appropriate AggregateActor with serialization-safe payload

**Interface Design**:
```csharp
public interface ISekibanDaprExecutor
{
    // Developer-facing API (accepts protobuf commands)
    Task<ResultBox<TResult>> ExecuteCommandAsync<TCommand, TResult>(
        TCommand command, 
        PartitionKeys partitionKeys)
        where TCommand : ICommand;
    
    // Query execution
    Task<ResultBox<TResult>> ExecuteQueryAsync<TQuery, TResult>(
        TQuery query)
        where TQuery : IQuery<TResult>;
}
```

#### 2.2 Serialization Envelope Types

**Purpose**: Provide a concrete, serialization-safe wrapper for actor communication.

```csharp
// Generic envelope for actor input
[GenerateSerializer]
public record ActorCommandEnvelope
{
    public string CommandTypeName { get; init; }
    public string SerializedPayload { get; init; } // JSON-packed protobuf
    public Dictionary<string, string> Metadata { get; init; }
    public PartitionKeys PartitionKeys { get; init; }
}

// Response envelope
[GenerateSerializer]
public record ActorResultEnvelope
{
    public bool Success { get; init; }
    public string? SerializedResult { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorType { get; init; }
}

// Event envelope for event handler actor
[GenerateSerializer]
public record ActorEventEnvelope
{
    public string EventTypeName { get; init; }
    public string SerializedPayload { get; init; } // JSON-packed protobuf
    public SortableUniqueId SortableUniqueId { get; init; }
    public PartitionKeys PartitionKeys { get; init; }
}
```

#### 2.3 AggregateActor Modifications

**Updated Interface**:
```csharp
public interface IAggregateActor : IActor
{
    // Simplified interface accepting only concrete envelope types
    Task<ActorResultEnvelope> HandleCommandAsync(ActorCommandEnvelope envelope);
    Task<AggregateState> GetStateAsync();
    Task<List<Event>> GetEventsAsync(SortableUniqueId? since);
}
```

**Implementation Strategy**:
```csharp
public class AggregateActor : Actor, IAggregateActor
{
    private readonly IProtobufSerializer _protobufSerializer;
    private readonly ICommandRegistry _commandRegistry;
    
    public async Task<ActorResultEnvelope> HandleCommandAsync(ActorCommandEnvelope envelope)
    {
        try
        {
            // 1. Deserialize protobuf from JSON-packed payload
            var commandType = _commandRegistry.GetType(envelope.CommandTypeName);
            var command = _protobufSerializer.Deserialize(
                envelope.SerializedPayload, 
                commandType);
            
            // 2. Execute command using existing Sekiban logic
            var result = await ExecuteCommandInternal(command, envelope.PartitionKeys);
            
            // 3. Pack result back to envelope
            return new ActorResultEnvelope
            {
                Success = result.IsSuccess,
                SerializedResult = result.IsSuccess 
                    ? _protobufSerializer.Serialize(result.Value) 
                    : null,
                ErrorMessage = result.Error?.Message,
                ErrorType = result.Error?.GetType().Name
            };
        }
        catch (Exception ex)
        {
            return new ActorResultEnvelope
            {
                Success = false,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name
            };
        }
    }
}
```

#### 2.4 Serialization Infrastructure

**IProtobufSerializer Interface**:
```csharp
public interface IProtobufSerializer
{
    string Serialize<T>(T obj) where T : IMessage;
    T Deserialize<T>(string json) where T : IMessage, new();
    object Deserialize(string json, Type messageType);
}

public class ProtobufJsonSerializer : IProtobufSerializer
{
    private readonly JsonFormatter _formatter;
    private readonly JsonParser _parser;
    
    public ProtobufJsonSerializer()
    {
        _formatter = new JsonFormatter(new JsonFormatter.Settings(true));
        _parser = new JsonParser(new JsonParser.Settings(100));
    }
    
    public string Serialize<T>(T obj) where T : IMessage
    {
        return _formatter.Format(obj);
    }
    
    public T Deserialize<T>(string json) where T : IMessage, new()
    {
        return _parser.Parse<T>(json);
    }
    
    public object Deserialize(string json, Type messageType)
    {
        var message = Activator.CreateInstance(messageType) as IMessage;
        return _parser.Parse(json, message.Descriptor);
    }
}
```

#### 2.5 Event Handling Flow

**AggregateEventHandlerActor Interface**:
```csharp
public interface IAggregateEventHandlerActor : IActor
{
    Task HandleEventAsync(ActorEventEnvelope envelope);
    Task<List<Event>> GetHandledEventsAsync(SortableUniqueId? since);
}
```

### 3. Project Structure

#### 3.1 src/Sekiban.Pure.Dapr Structure

```
src/Sekiban.Pure.Dapr/
├── Actors/
│   ├── IAggregateActor.cs
│   ├── AggregateActor.cs
│   ├── IAggregateEventHandlerActor.cs
│   └── AggregateEventHandlerActor.cs
├── Serialization/
│   ├── IProtobufSerializer.cs
│   ├── ProtobufJsonSerializer.cs
│   ├── ActorCommandEnvelope.cs
│   ├── ActorResultEnvelope.cs
│   └── ActorEventEnvelope.cs
├── Executor/
│   ├── ISekibanDaprExecutor.cs
│   └── SekibanDaprExecutor.cs
├── Registry/
│   ├── ICommandRegistry.cs
│   ├── CommandRegistry.cs
│   ├── IEventRegistry.cs
│   └── EventRegistry.cs
└── Extensions/
    └── ServiceCollectionExtensions.cs
```

#### 3.2 Test Project Structure

```
tests/Sekiban.Pure.Dapr.Tests/
├── Serialization/
│   ├── ProtobufSerializerTests.cs
│   ├── EnvelopeSerializationTests.cs
│   └── Fixtures/
│       └── TestProtobufMessages.proto
├── Actors/
│   ├── AggregateActorTests.cs
│   ├── EventHandlerActorTests.cs
│   └── Mocks/
│       └── MockActorHost.cs
├── Executor/
│   └── SekibanDaprExecutorTests.cs
└── Integration/
    └── EndToEndFlowTests.cs
```

#### 3.3 Sample Project Structure

```
internalUsages/DaprSample/
├── Domain/
│   ├── Aggregates/
│   │   └── User/
│   │       ├── Commands/
│   │       │   ├── CreateUser.proto
│   │       │   └── UpdateUser.proto
│   │       ├── Events/
│   │       │   ├── UserCreated.proto
│   │       │   └── UserUpdated.proto
│   │       └── UserProjector.cs
│   └── Generated/
│       └── Protobuf/ (generated protobuf classes)
├── Infrastructure/
│   ├── DaprConfiguration.cs
│   └── StartupExtensions.cs
├── Api/
│   ├── Controllers/
│   │   └── UserController.cs
│   └── Program.cs
└── Tests/
    ├── UserScenarioTests.cs
    └── PerformanceTests.cs
```

### 4. Implementation Phases

#### Phase 1: Core Serialization Infrastructure
1. Implement `IProtobufSerializer` and `ProtobufJsonSerializer`
2. Create envelope types with proper serialization attributes
3. Unit tests for serialization/deserialization

#### Phase 2: Actor Implementation
1. Refactor `IAggregateActor` to use envelope types
2. Implement command deserialization and execution logic
3. Implement event serialization and forwarding
4. Actor-level unit tests

#### Phase 3: Executor Implementation
1. Implement `SekibanDaprExecutor` with protobuf support
2. Wire up command/query execution paths
3. Integration tests for executor

#### Phase 4: Event Handler Actor
1. Implement `IAggregateEventHandlerActor`
2. Event processing and persistence logic
3. Event sourcing integration tests

#### Phase 5: Sample Application
1. Create DaprSample with protobuf definitions
2. Implement domain logic using new serialization
3. End-to-end scenario tests
4. Performance benchmarks

### 5. Configuration

**appsettings.json**:
```json
{
  "Sekiban": {
    "Dapr": {
      "ActorType": "AggregateActor",
      "EventHandlerActorType": "EventHandlerActor",
      "Serialization": {
        "UseProtobuf": true,
        "MaxMessageSize": 4194304,
        "CompressionEnabled": true
      }
    }
  }
}
```

**Service Registration**:
```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSekibanDapr(
        this IServiceCollection services,
        Action<SekibanDaprOptions>? configure = null)
    {
        var options = new SekibanDaprOptions();
        configure?.Invoke(options);
        
        services.AddSingleton<IProtobufSerializer, ProtobufJsonSerializer>();
        services.AddSingleton<ICommandRegistry, CommandRegistry>();
        services.AddSingleton<IEventRegistry, EventRegistry>();
        services.AddScoped<ISekibanDaprExecutor, SekibanDaprExecutor>();
        
        services.AddActors(actorOptions =>
        {
            actorOptions.Actors.RegisterActor<AggregateActor>();
            actorOptions.Actors.RegisterActor<AggregateEventHandlerActor>();
        });
        
        return services;
    }
}
```

### 6. Testing Strategy

#### Unit Tests
- Serialization round-trip tests for all envelope types
- Actor method isolation tests with mocked dependencies
- Command/Event registry tests

#### Integration Tests
- Full command execution flow through Dapr actors
- Event persistence and retrieval
- Multi-actor coordination scenarios

#### Performance Tests
- Serialization performance benchmarks
- Actor throughput measurements
- Memory usage profiling

### 7. Migration Path

For existing implementations:
1. Maintain backward compatibility with non-protobuf commands
2. Provide adapter layer for gradual migration
3. Document protobuf schema generation from existing C# types

### 8. Security Considerations

- Validate protobuf message size limits
- Implement message signing for actor communication
- Audit logging for all actor invocations
- Input validation at deserialization boundaries

### 9. Monitoring and Observability

- OpenTelemetry integration for actor invocations
- Custom metrics for serialization performance
- Distributed tracing across actor boundaries
- Health checks for actor availability

### 10. Future Enhancements

1. Binary protobuf support (in addition to JSON-packed)
2. Message compression options
3. Batch command processing
4. Actor state snapshots with protobuf serialization
5. gRPC service layer for external integration