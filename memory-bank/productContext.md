# Product Context: Sekiban

## Why Sekiban Exists

Sekiban was created to address the challenges developers face when implementing Event Sourcing and CQRS patterns in .NET applications. While these architectural patterns offer significant benefits for complex domain models and distributed systems, they often require substantial boilerplate code and infrastructure setup.

The name "Sekiban" (石盤) means "stone tablet" in Japanese, symbolizing the immutable, permanent record of events that is central to event sourcing.

## Problems Sekiban Solves

### 1. Complexity of Event Sourcing Implementation
Event Sourcing requires careful management of event streams, state reconstruction, and concurrency control. Sekiban provides a structured framework that handles these concerns, allowing developers to focus on domain logic.

### 2. Storage Infrastructure Challenges
Setting up and optimizing event stores can be complex. Sekiban abstracts away the storage details, supporting multiple backends (Cosmos DB, DynamoDB, PostgreSQL) with a consistent API.

### 3. Performance Concerns
Event sourcing can face performance challenges when reconstructing state from long event streams. Sekiban addresses this with features like projection snapshots and optimized query capabilities.

### 4. Distributed System Coordination
With its Orleans integration, Sekiban.Pure simplifies building distributed, event-sourced systems that can scale horizontally while maintaining consistency.

### 5. Developer Experience
Implementing event sourcing from scratch requires significant expertise. Sekiban provides a declarative, type-safe API that guides developers toward correct implementations.

## How Sekiban Works

### Core Architecture

1. **Command Handling**
   - Commands represent user intentions to change the system
   - Commands are validated and produce events
   - Commands don't modify state directly

2. **Event Processing**
   - Events are immutable facts about what happened
   - Events are stored in an event store (Cosmos DB, DynamoDB, PostgreSQL)
   - Events are the source of truth for the system

3. **State Projection**
   - Projectors apply events to build the current state
   - Different projections can be created for different query needs
   - Snapshots can be taken to optimize performance

4. **Query Handling**
   - Queries read from projected state
   - Queries can filter and sort data
   - Multiple projection types support different query patterns

### Key Workflows

1. **Command Execution Flow**
   - Client sends command
   - Command handler validates command
   - If valid, events are generated
   - Events are stored in the event store
   - Events are applied to update projections
   - Command result is returned to client

2. **Query Execution Flow**
   - Client sends query
   - Query handler retrieves data from projections
   - Data is filtered and sorted
   - Results are returned to client

3. **Aggregate Lifecycle**
   - Aggregates are created via commands
   - State changes are tracked as events
   - Current state is reconstructed by replaying events
   - Optimistic concurrency control prevents conflicts

## User Experience Goals

Sekiban aims to provide:

1. **Intuitive API**
   - Clear, consistent interfaces for defining domain components
   - Type-safe operations with compile-time checks
   - Minimal boilerplate code

2. **Flexibility**
   - Support for different storage backends
   - Customizable serialization
   - Integration with existing systems

3. **Performance**
   - Efficient state reconstruction
   - Optimized query capabilities
   - Scalable distributed processing with Orleans

4. **Reliability**
   - Consistent event handling
   - Proper concurrency control
   - Resilient storage options

5. **Developer Productivity**
   - Templates for quick project setup
   - Built-in testing support
   - Comprehensive documentation and examples
