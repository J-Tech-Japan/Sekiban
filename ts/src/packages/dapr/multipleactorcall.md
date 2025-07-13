# Dapr JavaScript SDK - Multiple Actor Registration Issue

## Problem Description

When using the Dapr JavaScript SDK (versions 3.3.0 and 3.5.2), only the first actor registered with `DaprServer.actor.registerActor()` is actually recognized by the Dapr runtime. Subsequent actor registrations appear to succeed in the application logs but are ignored by Dapr.

## Environment

- **Dapr JavaScript SDK**: v3.3.0 and v3.5.2 (tested on both)
- **Dapr Runtime**: v1.15.6
- **Node.js**: v23.10.0
- **Platform**: macOS (Darwin 24.5.0)
- **TypeScript**: v5.x

## Expected Behavior

When registering multiple actors like this:

```typescript
const daprServer = new DaprServer({
  serverHost: "127.0.0.1",
  serverPort: "3001",
  communicationProtocol: CommunicationProtocolEnum.HTTP,
  clientOptions: {
    daprHost: "127.0.0.1",
    daprPort: "3501"
  }
});

// Register multiple actors
await daprServer.actor.registerActor(AggregateActor);
await daprServer.actor.registerActor(AggregateEventHandlerActor);
await daprServer.actor.registerActor(DummyActor);

// Start the server
await daprServer.start();
```

All three actors should be registered and available for invocation.

## Actual Behavior

Only the first actor (AggregateActor) is registered. The Dapr runtime logs show:

```
time="2025-07-11T03:51:30.872389-07:00" level=info msg="Registering hosted actors: [AggregateActor]" app_id=dapr-sample-api
```

The `/dapr/config` endpoint returns:

```json
{
  "entities": ["AggregateActor"],
  "actorIdleTimeout": "1h",
  "drainOngoingCallTimeout": "30s",
  "drainRebalancedActors": true
}
```

## Reproduction Steps

### Minimal Reproduction

1. Create a simple Node.js/TypeScript project with Dapr SDK
2. Define multiple actor classes:

```typescript
import { AbstractActor, DaprClient, ActorId } from '@dapr/dapr';

export class ActorOne extends AbstractActor {
  static get actorType() { return "ActorOne"; }
  
  async testMethod(): Promise<string> {
    return "ActorOne response";
  }
}

export class ActorTwo extends AbstractActor {
  static get actorType() { return "ActorTwo"; }
  
  async testMethod(): Promise<string> {
    return "ActorTwo response";
  }
}
```

3. Register both actors:

```typescript
const daprServer = new DaprServer({ /* config */ });
await daprServer.actor.registerActor(ActorOne);
await daprServer.actor.registerActor(ActorTwo);
await daprServer.start();
```

4. Check `/dapr/config` - only ActorOne will be listed

### Workaround Attempts That Failed

1. **Using explicit `actor.init()` call**:
```typescript
await daprServer.actor.registerActor(ActorOne);
await daprServer.actor.registerActor(ActorTwo);
await daprServer.actor.init(); // Does not help
```

2. **Changing registration order** - Always only the first actor is registered

3. **Simplifying actor implementation** - Even the simplest actors exhibit this behavior

4. **Different actor types** - The issue persists regardless of actor complexity

## Observed Behavior Differences

### Simple Test Application
In a minimal test application with just actor registration and no other dependencies, the workaround of calling `registerActor()` explicitly works:

```typescript
// This works in simple apps
server.actor.registerActor(WorkerActor);
server.actor.registerActor(CoordinatorActor);
```

### Complex Application (with Express, routing, etc.)
In more complex applications with Express integration and other middleware, only the first actor is registered regardless of workarounds attempted.

## Impact

This issue prevents implementing actor-to-actor communication patterns where different actors have different responsibilities. For example:
- Cannot separate aggregate command handling from event persistence
- Cannot implement saga/process manager patterns with dedicated actors
- Forces all actor logic into a single actor class, reducing modularity

## Code Examples

### What Should Work But Doesn't

```typescript
// Actor 1: Handles commands
class AggregateActor extends AbstractActor {
  async executeCommand(command: any) {
    // Process command, generate events
    const events = [...];
    
    // Call EventHandlerActor to persist events
    const eventHandler = this.actorProxyFactory<IEventHandlerActor>(
      'EventHandlerActor',
      aggregateId
    );
    await eventHandler.appendEvents(events); // This fails - actor not found
  }
}

// Actor 2: Handles event persistence
class EventHandlerActor extends AbstractActor {
  async appendEvents(events: any[]) {
    // Persist events to storage
  }
}
```

### Current Workaround Required

All functionality must be combined into a single actor:

```typescript
class CombinedActor extends AbstractActor {
  async executeCommand(command: any) {
    // Process command
    // AND handle event persistence
    // All in one actor - not ideal for separation of concerns
  }
}
```

## Questions for the Community

1. Is this a known issue with the Dapr JavaScript SDK?
2. Are there any undocumented workarounds for registering multiple actors?
3. Is this behavior different in other Dapr SDK implementations (e.g., .NET, Python)?
4. Is there a recommended pattern for actor-to-actor communication in JavaScript/TypeScript when only one actor type can be registered?

## Additional Context

- The issue appears to be in the SDK's actor registration mechanism, not in the Dapr runtime itself
- Other Dapr SDKs (e.g., .NET) support multiple actors without issues
- This significantly limits the architectural patterns available when using Dapr with JavaScript/TypeScript

## Related Information

- Dapr JS SDK Repository: https://github.com/dapr/js-sdk
- Tested versions: v3.3.0, v3.5.2 (latest as of March 2025)
- No existing GitHub issues found specifically addressing this problem