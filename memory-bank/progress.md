# Progress: Sekiban

## What Works

### Core Functionality

1. **Event Sourcing Engine**
   - Event storage and retrieval
   - Aggregate state reconstruction
   - Optimistic concurrency control
   - Event versioning

2. **Command Processing**
   - Command validation
   - Event generation
   - Command execution pipeline
   - Error handling and result boxing

3. **Projection System**
   - Single-aggregate projections
   - Multi-aggregate projections
   - Projection snapshots
   - State transitions with different payload types

4. **Query Capabilities**
   - List queries with filtering and sorting
   - Single-result queries
   - Query execution pipeline
   - Result transformation

### Storage Backends

1. **Azure Cosmos DB**
   - Event storage
   - Projection storage
   - Hierarchical partition keys
   - Change feed integration

2. **Amazon DynamoDB**
   - Event storage
   - Projection storage
   - Partition key and sort key organization

3. **PostgreSQL**
   - Event storage
   - Projection storage
   - ACID-compliant transactions

### Integration and Extensions

1. **Orleans Integration**
   - Virtual actor model
   - Distributed processing
   - Grain persistence
   - Cluster configuration

2. **.NET Aspire Support**
   - Service orchestration
   - Configuration management
   - Service discovery
   - Development dashboard

3. **API Generation**
   - Command endpoints
   - Query endpoints
   - OpenAPI/Swagger documentation
   - Result handling

4. **Testing Support**
   - Command testing
   - Projection testing
   - Event replay testing
   - Test fixtures and utilities

## What's Left to Build

### Core Enhancements

1. **Event Management Features**
   - Event removal functionality (implemented for InMemoryEventWriter)
   - Event filtering capabilities
   - Event transformation utilities
   - Event lifecycle management

2. **Materialized View Helpers**
   - Database-specific view creation
   - Change feed integration
   - View management utilities
   - Performance optimization

3. **Event Schema Evolution**
   - Event upcasters
   - Schema migration tools
   - Compatibility layer
   - Version management

4. **Advanced Projection Features**
   - Projection dependencies
   - Conditional projections
   - Projection pruning
   - Custom projection storage

### Infrastructure and Integration

1. **Cloud Deployment Templates**
   - Azure deployment templates
   - AWS deployment templates
   - Infrastructure-as-code examples
   - Deployment documentation

2. **Monitoring and Observability**
   - Telemetry integration
   - Performance metrics
   - Health checks
   - Logging enhancements

3. **Security Enhancements**
   - Authentication integration
   - Authorization framework
   - Data encryption
   - Audit logging

### Developer Experience

1. **Enhanced Documentation**
   - Comprehensive guides
   - Best practices documentation
   - Pattern catalogs
   - Troubleshooting guides

2. **Additional Templates**
   - Domain-specific templates
   - Microservice templates
   - Integration templates
   - Testing templates

3. **Developer Tools**
   - Event viewer
   - Projection explorer
   - Command/query testing tools
   - Debugging utilities

## Current Status

### Sekiban Core

- **Status**: Stable
- **Version**: 0.15.x
- **Compatibility**: .NET 8+
- **Maturity**: Production-ready for small to medium-sized systems
- **Documentation**: Basic documentation available

### Sekiban.Pure.Orleans

- **Status**: Active Development
- **Version**: Early release
- **Compatibility**: .NET 8+, Orleans 8.0+
- **Maturity**: Suitable for experimental and development use
- **Documentation**: Initial documentation available

### Project Templates

- **Status**: Available
- **Templates**: Basic Orleans+Aspire template
- **Compatibility**: .NET 8+
- **Maturity**: Ready for use in new projects
- **Documentation**: Template documentation available

## Known Issues

### Technical Limitations

1. **Large-Scale Performance**
   - Live projections may face performance issues with very large data sets
   - Multi-aggregate projections must fit in memory
   - Long event streams can impact reconstruction performance

2. **Storage Constraints**
   - Document size limits in Cosmos DB (2MB) and DynamoDB (400KB)
   - Large snapshots require blob storage integration
   - Query performance depends on database-specific optimizations

3. **Distributed Processing**
   - Orleans configuration requires careful tuning for production
   - Cluster management adds operational complexity
   - Grain persistence strategy impacts performance and reliability

### Development Challenges

1. **Learning Curve**
   - Event sourcing concepts require understanding
   - Type-safe API can be complex for newcomers
   - Orleans integration adds additional complexity

2. **Testing Complexity**
   - Event-sourced systems require different testing approaches
   - Distributed testing adds complexity
   - Temporal aspects can be challenging to test

3. **Integration Challenges**
   - Integration with non-event-sourced systems requires careful design
   - Hybrid architectures add complexity
   - Migration from traditional architectures can be challenging

## Roadmap Highlights

### Short-term (Next 3 Months)

1. **Event Management Enhancements**
   - ✅ Implement event removal functionality for InMemoryEventWriter
   - ✅ Add unit tests for event removal
   - Extend event removal to other storage backends (Cosmos DB, DynamoDB, PostgreSQL)
   - Document event management capabilities
   - Implement additional event management features (filtering, transformation)

2. **Sekiban.Pure.Orleans Stabilization**
   - Bug fixes and performance improvements
   - Enhanced documentation
   - Additional examples

3. **PostgreSQL Optimization**
   - Performance enhancements
   - Advanced query capabilities
   - Migration tools

4. **Documentation Expansion**
   - Comprehensive guides
   - Best practices documentation
   - Troubleshooting guides

### Medium-term (3-6 Months)

1. **Materialized View Helpers**
   - Database-specific view creation
   - Change feed integration
   - View management utilities

2. **Enhanced Testing Support**
   - Additional testing utilities
   - Distributed testing helpers
   - Performance testing tools

3. **Additional Templates**
   - Domain-specific templates
   - Microservice templates
   - Integration templates

### Long-term (6+ Months)

1. **Event Schema Evolution**
   - Event upcasters
   - Schema migration tools
   - Compatibility layer

2. **Cloud Deployment Templates**
   - Azure deployment templates
   - AWS deployment templates
   - Infrastructure-as-code examples

3. **Developer Tools**
   - Event viewer
   - Projection explorer
   - Command/query testing tools
