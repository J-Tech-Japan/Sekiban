# Active Context: Sekiban

## Current Work Focus

The current focus for Sekiban is on the newer Sekiban.Pure version, which integrates with Microsoft Orleans for distributed processing and state management. This version represents a more functional approach to event sourcing and provides better support for distributed systems.

Key areas of active development include:

1. **Orleans Integration**
   - Enhancing the integration with Microsoft Orleans
   - Optimizing grain persistence strategies
   - Improving distributed processing capabilities

2. **Aspire Support**
   - Integrating with .NET Aspire for cloud-ready applications
   - Providing templates and examples for Aspire-based deployments
   - Streamlining the development experience with Aspire

3. **PostgreSQL Support**
   - Expanding and optimizing PostgreSQL as a storage backend
   - Ensuring compatibility with the latest PostgreSQL versions
   - Providing migration tools and guidance

4. **Documentation and Examples**
   - Improving documentation for developers
   - Creating more comprehensive examples
   - Providing guidance for common scenarios and patterns

## Recent Changes

Recent significant changes to the Sekiban project include:

1. **Sekiban.Pure.Orleans**
   - Introduction of the new Sekiban.Pure.Orleans version
   - More functional approach to event sourcing
   - Integration with Microsoft Orleans for distributed processing

2. **Project Templates**
   - New project templates for quick setup
   - Support for Orleans and Aspire integration
   - Templates for different storage backends

3. **State Machine Pattern**
   - Enhanced support for the state machine pattern
   - Type-safe state transitions using different payload types
   - Compile-time enforcement of state-dependent operations

4. **.NET 8 and 9 Support**
   - Updated to support the latest .NET versions
   - Leveraging new language features and performance improvements
   - Ensuring compatibility with modern .NET development practices

## Next Steps

Planned next steps for the Sekiban project include:

1. **Materialized View Helpers**
   - Building helpers for creating and managing materialized views
   - Leveraging database-specific features for efficient view creation
   - Providing patterns and best practices for view management

2. **Performance Optimizations**
   - Enhancing performance for large event streams
   - Optimizing projection rebuilding
   - Improving query performance for complex scenarios

3. **Enhanced Testing Support**
   - Expanding the built-in testing framework
   - Providing more testing utilities and helpers
   - Simplifying the testing of event-sourced systems

4. **Cloud Deployment Templates**
   - Creating templates for deploying to Azure and AWS
   - Providing infrastructure-as-code examples
   - Documenting best practices for cloud deployments

5. **Event Schema Evolution**
   - Improving support for event schema evolution
   - Providing tools for managing breaking changes
   - Documenting patterns for maintaining backward compatibility

## Active Decisions and Considerations

Key decisions and considerations currently being evaluated:

1. **Storage Strategy**
   - Evaluating the trade-offs between different storage backends
   - Considering performance, cost, and scalability implications
   - Determining best practices for different scenarios

2. **Projection Optimization**
   - Exploring strategies for optimizing projections
   - Considering in-memory vs. materialized approaches
   - Evaluating performance implications for different projection types

3. **API Design**
   - Refining the API design for better developer experience
   - Ensuring consistency across different components
   - Balancing flexibility and simplicity

4. **Orleans Configuration**
   - Determining optimal Orleans configuration for different scenarios
   - Evaluating clustering and persistence options
   - Considering performance and reliability trade-offs

5. **Multi-Tenancy Approach**
   - Refining the approach to multi-tenancy
   - Evaluating partition key strategies for tenant isolation
   - Considering security and performance implications

## Current Challenges

Challenges currently being addressed:

1. **Large-Scale Performance**
   - Ensuring performance at scale for systems with many events
   - Optimizing for high-throughput scenarios
   - Addressing potential bottlenecks in distributed processing

2. **Developer Onboarding**
   - Simplifying the learning curve for new developers
   - Providing clear documentation and examples
   - Creating intuitive APIs and patterns

3. **Event Schema Evolution**
   - Managing changes to event schemas over time
   - Ensuring backward compatibility
   - Providing tools for event migration and versioning

4. **Integration with Existing Systems**
   - Facilitating integration with non-event-sourced systems
   - Providing patterns for hybrid architectures
   - Supporting gradual migration to event sourcing

5. **Testing Complexity**
   - Addressing the complexity of testing event-sourced systems
   - Providing tools and patterns for effective testing
   - Simplifying the testing of distributed scenarios
