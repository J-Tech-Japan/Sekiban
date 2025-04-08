# Technical Context: Sekiban

## Technologies Used

### Core Technologies

1. **C# / .NET**
   - Sekiban is built with C# and targets .NET 8 and 9
   - Leverages modern C# features like records, pattern matching, and nullable reference types
   - Uses .NET's dependency injection system for component registration

2. **Event Sourcing**
   - Core architectural pattern where all changes are stored as immutable events
   - Events are the source of truth for the system
   - Current state is derived by replaying events

3. **CQRS (Command Query Responsibility Segregation)**
   - Separates read and write operations
   - Commands change state, queries retrieve state
   - Enables optimization of each path independently

4. **Microsoft Orleans**
   - Virtual actor model for distributed systems
   - Used in Sekiban.Pure for scalable, distributed processing
   - Provides a programming model for building distributed applications

### Storage Technologies

1. **Microsoft Azure Cosmos DB**
   - NoSQL database with global distribution
   - Used as an event store in Sekiban
   - Supports hierarchical partition keys for efficient querying
   - Change feed feature can be used for materialized views

2. **Amazon DynamoDB**
   - AWS's managed NoSQL database service
   - Alternative event store option in Sekiban
   - Uses partition key and sort key for data organization

3. **PostgreSQL**
   - Relational database option for event storage
   - Provides ACID compliance and transaction support
   - Used in Sekiban.Pure as an alternative to document databases

4. **Azure Blob Storage / Amazon S3**
   - Object storage services
   - Used for storing large snapshots that exceed document size limits

### Development and Integration

1. **.NET Aspire**
   - Cloud-ready stack for building distributed applications
   - Integrated with Sekiban.Pure.Orleans for orchestration
   - Provides service discovery and configuration

2. **System.Text.Json**
   - Modern JSON serialization library
   - Used with source generation for efficient serialization
   - Supports both runtime and AOT compilation scenarios

3. **OpenAPI / Swagger**
   - API documentation and testing tools
   - Integrated with Sekiban's web API generation
   - Provides interactive documentation for API consumers

## Development Setup

### Prerequisites

- .NET SDK 8.0 or later
- IDE with C# support (Visual Studio, VS Code with C# extension, JetBrains Rider)
- Database access (Cosmos DB, DynamoDB, or PostgreSQL)
- For Orleans: local development cluster or cloud deployment

### Project Templates

Sekiban provides templates for quick project setup:

```bash
# Install templates
dotnet new install Sekiban.Pure.Templates

# Create a new project with Orleans and Aspire
dotnet new sekiban-orleans-aspire -n MyProject
```

### Project Structure

A typical Sekiban project follows this structure:

```
MyProject/
├── MyProject.Domain/             # Domain model, events, commands
│   ├── Aggregates/               # Aggregate definitions
│   ├── Commands/                 # Command handlers
│   ├── Events/                   # Event definitions
│   ├── Projectors/               # State projectors
│   ├── Queries/                  # Query handlers
│   └── MyProjectEventsJsonContext.cs  # JSON serialization context
├── MyProject.ApiService/         # API endpoints
│   ├── Program.cs                # Application entry point
│   └── appsettings.json          # Configuration
├── MyProject.Web/                # Web frontend (optional)
├── MyProject.AppHost/            # Aspire host (for Orleans)
└── MyProject.ServiceDefaults/    # Common service configurations
```

### Configuration

Sekiban uses standard .NET configuration patterns with `appsettings.json`:

```json
{
  "Sekiban": {
    "Database": "Cosmos",  // or "Postgres", "Dynamo"
    "ConnectionString": "your-connection-string",
    "DatabaseName": "your-database-name"
  }
}
```

## Technical Constraints

### Storage Limitations

1. **Document Size Limits**
   - Cosmos DB: 2MB per document
   - DynamoDB: 400KB per item
   - Large snapshots must use blob storage (Azure Blob Storage, S3)

2. **Query Performance**
   - Live projections work well for small to medium-sized systems
   - Large systems may need materialized views for optimal query performance
   - Consider database-specific query optimization techniques

### Scalability Considerations

1. **Event Stream Length**
   - Very long event streams can impact performance
   - Use snapshots to optimize state reconstruction
   - Consider event versioning for long-lived aggregates

2. **Projection Size**
   - Multi-aggregate projections must fit in memory
   - Large projections may require custom optimization
   - Consider using materialized views for large-scale data

3. **Distributed Processing**
   - Orleans provides scalability but adds complexity
   - Ensure proper silo configuration for production deployments
   - Consider grain persistence strategy based on expected load

### Compatibility

1. **Framework Versions**
   - Sekiban targets .NET 8 and 9
   - Sekiban.Pure requires Orleans 8.0 or later
   - Database SDKs have their own version requirements

2. **API Compatibility**
   - Event schema evolution requires careful planning
   - Use event versioning for forward compatibility
   - Consider using event upcasters for complex migrations

## Dependencies

### Core Dependencies

1. **Sekiban.Core / Sekiban.Pure**
   - Core framework libraries
   - Provides base interfaces and implementations
   - Handles command and query execution

2. **Microsoft.Orleans**
   - Required for Sekiban.Pure.Orleans
   - Provides the virtual actor model
   - Handles distributed processing and state management

3. **System.Text.Json**
   - JSON serialization library
   - Used for event and state serialization
   - Supports source generation for performance

### Storage Dependencies

1. **Microsoft.Azure.Cosmos**
   - Cosmos DB SDK
   - Required for Cosmos DB storage

2. **AWSSDK.DynamoDBv2**
   - DynamoDB SDK
   - Required for DynamoDB storage

3. **Npgsql**
   - PostgreSQL client library
   - Required for PostgreSQL storage

4. **Azure.Storage.Blobs / AWSSDK.S3**
   - Blob storage SDKs
   - Required for large snapshot storage

### Integration Dependencies

1. **Aspire.Hosting**
   - .NET Aspire hosting library
   - Used for service orchestration

2. **Swashbuckle.AspNetCore**
   - OpenAPI/Swagger integration
   - Used for API documentation

3. **Microsoft.Extensions.DependencyInjection**
   - Dependency injection container
   - Used for component registration

## Development Tools

1. **dotnet CLI**
   - Command-line interface for .NET development
   - Used for building, testing, and running applications

2. **Visual Studio / VS Code / Rider**
   - IDEs with C# support
   - Provide debugging and development tools

3. **Azure Portal / AWS Console**
   - Cloud service management interfaces
   - Used for configuring and monitoring cloud resources

4. **Orleans Dashboard**
   - Monitoring tool for Orleans clusters
   - Provides insights into grain activations and performance
