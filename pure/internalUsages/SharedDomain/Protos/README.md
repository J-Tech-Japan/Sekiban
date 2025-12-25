# Protobuf Definitions for DaprSample

This directory contains Protocol Buffer definitions for the DaprSample domain.

## Structure

- `account_commands.proto` - Command messages for account operations
- `account_events.proto` - Event messages that record what happened
- `account_aggregates.proto` - Aggregate state definitions

## Usage

The proto files are automatically compiled when you build the project. The generated C# classes will be in the namespace
specified by `option csharp_namespace`.

### Example Command Usage

```csharp
// Create a Protobuf command
var createAccountCmd = new CreateAccount
{
    AccountId = Guid.NewGuid().ToString(),
    AccountName = "Savings Account",
    AccountType = "SAVINGS",
    InitialBalance = 1000.0,
    Currency = "USD"
};

// The command will be serialized to Protobuf when sent to actors
```

### Example Event Usage

```csharp
// Events are created by the system, not manually
// But you can deserialize them from Protobuf:
var accountCreatedEvent = AccountCreated.Parser.ParseFrom(eventBytes);
```

## Integration with Sekiban

The Protobuf messages work seamlessly with Sekiban's envelope-based actor communication:

1. Commands are wrapped in `CommandEnvelope` with Protobuf payload
2. Events are wrapped in `EventEnvelope` with Protobuf payload
3. Actors communicate using these envelopes for proper Dapr serialization

## Adding New Types

1. Create a new `.proto` file in this directory
2. Define your messages following Protobuf conventions
3. Build the project to generate C# classes
4. Register the types with the Protobuf type mapper if needed