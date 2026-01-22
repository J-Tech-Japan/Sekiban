# Implementation Decisions

This document records the implementation decisions made for Issue #876.

**Date**: 2026-01-22
**Status**: Approved for Implementation

---

## Summary of Decisions

| Decision | Choice | Notes |
|----------|--------|-------|
| Table Creation | AutoCreate | Skip if already exists (DescribeTable first) |
| S3 Offload Package | Same PR | Implement together with DynamoDB package |
| Local Development | LocalStack | Primary dev/test environment |
| Aspire Integration | Basic only | Standard DI registration, Aspire later |
| Write Sharding | Configurable (default=1) | User configures if needed |
| Sample Project | Yes - with AppHost | DynamoDB version of DcbOrleans |
| Test Scope | Unit + Integration | Mock tests + LocalStack integration |

---

## 1. Table Creation Strategy

**Decision**: AutoCreate with existence check

```csharp
public async Task CreateTableIfNotExistsAsync(string tableName, CreateTableRequest schema)
{
    try
    {
        await _client.DescribeTableAsync(tableName);
        _logger.LogDebug("Table {TableName} already exists, skipping creation", tableName);
    }
    catch (ResourceNotFoundException)
    {
        await _client.CreateTableAsync(schema);
        await WaitForTableActiveAsync(tableName);
        _logger.LogInformation("Table {TableName} created successfully", tableName);
    }
}
```

**Behavior**:
- Infrastructure-managed tables → Skipped (no error)
- Non-existent tables → Created automatically
- Schema validation → Not performed (same as Cosmos DB)

---

## 2. Package Scope (Same PR)

Both packages will be implemented in the same PR:

```
dcb/src/
├── Sekiban.Dcb.DynamoDB/           # Event store
└── Sekiban.Dcb.BlobStorage.S3/     # Projection state offload
```

**Rationale**: 400KB DynamoDB item limit makes S3 offload essential for production use.

---

## 3. Local Development Environment

**Primary**: LocalStack

```yaml
# docker-compose.yml
services:
  localstack:
    image: localstack/localstack:latest
    ports:
      - "4566:4566"
    environment:
      - SERVICES=dynamodb,s3
```

**Configuration**:
```json
{
  "DynamoDb": {
    "ServiceURL": "http://localhost:4566"
  },
  "S3BlobStorage": {
    "ServiceUrl": "http://localhost:4566",
    "ForcePathStyle": true
  }
}
```

---

## 4. Aspire Integration

**Decision**: Basic only (for initial implementation)

```csharp
// Implemented
services.AddSekibanDcbDynamoDb(configuration);
services.AddSekibanDcbS3BlobStorage(configuration);

// NOT implemented (future enhancement)
// services.AddSekibanDcbDynamoDbWithAspire();
```

---

## 5. Write Sharding Configuration

**Default**: Disabled (WriteShardCount = 1)

```csharp
public class DynamoDbEventStoreOptions
{
    /// <summary>
    /// Number of shards for GSI partition key.
    /// Default: 1 (no sharding)
    /// Set higher for high-throughput scenarios.
    /// </summary>
    public int WriteShardCount { get; set; } = 1;
}
```

**User documentation**: Recommend increasing for > 1000 writes/second.

---

## 6. Sample Project Structure

Create DynamoDB version of DcbOrleans sample:

```
internalUsages/
├── DcbOrleans.AppHost/           # Existing (Postgres)
├── DcbOrleansDynamoDB.AppHost/   # NEW - DynamoDB version
├── DcbOrleansDynamoDB.Api/       # NEW - API with DynamoDB
└── DcbOrleansDynamoDB.Web/       # NEW - Web frontend (optional, share with existing)
```

**AppHost will include**:
- LocalStack container configuration
- DynamoDB + S3 setup
- Orleans silo with DynamoDB grain storage (if applicable)

---

## 7. Test Project Structure

```
dcb/tests/
├── Sekiban.Dcb.DynamoDB.Tests/
│   ├── Unit/
│   │   ├── DynamoDbEventStoreTests.cs      # Mock-based tests
│   │   ├── DynamoDbContextTests.cs
│   │   └── IdempotencyTokenTests.cs
│   └── Integration/
│       ├── LocalStackFixture.cs            # Test container setup
│       ├── EventStoreIntegrationTests.cs   # Full round-trip tests
│       └── TagQueryIntegrationTests.cs
└── Sekiban.Dcb.BlobStorage.S3.Tests/
    ├── Unit/
    │   └── S3BlobStorageAccessorTests.cs
    └── Integration/
        └── S3IntegrationTests.cs
```

**Test Dependencies**:
```xml
<PackageReference Include="Testcontainers.LocalStack" Version="3.*" />
<PackageReference Include="Moq" Version="4.*" />
<PackageReference Include="xunit" Version="2.*" />
```

---

## 8. Implementation Order

1. **Phase 1: Core Packages**
   - [ ] `Sekiban.Dcb.DynamoDB` - Event store implementation
   - [ ] `Sekiban.Dcb.BlobStorage.S3` - S3 offload implementation

2. **Phase 2: Tests**
   - [ ] Unit tests with mocks
   - [ ] Integration tests with LocalStack

3. **Phase 3: Sample Project**
   - [ ] `DcbOrleansDynamoDB.AppHost` with LocalStack
   - [ ] API project configured for DynamoDB

4. **Phase 4: Documentation**
   - [ ] README for each package
   - [ ] Update main documentation

---

## 9. Project References

### Sekiban.Dcb.DynamoDB.csproj

```xml
<ItemGroup>
  <PackageReference Include="AWSSDK.DynamoDBv2" Version="3.*" />
  <PackageReference Include="AWSSDK.Extensions.NETCore.Setup" Version="3.*" />
  <ProjectReference Include="..\Sekiban.Dcb.Core\Sekiban.Dcb.Core.csproj"/>
</ItemGroup>
```

### Sekiban.Dcb.BlobStorage.S3.csproj

```xml
<ItemGroup>
  <PackageReference Include="AWSSDK.S3" Version="3.*" />
  <PackageReference Include="AWSSDK.Extensions.NETCore.Setup" Version="3.*" />
  <ProjectReference Include="..\Sekiban.Dcb.Core\Sekiban.Dcb.Core.csproj"/>
</ItemGroup>
```

---

## 10. Acceptance Criteria Checklist

From Issue #876:

- [ ] DynamoDB event store implementation
  - [ ] `IEventStore` implementation
  - [ ] `IMultiProjectionStateStore` implementation
  - [ ] Table auto-creation
  - [ ] Transaction support (TransactWriteItems)
  - [ ] Idempotency token generation

- [ ] Same API surface as Cosmos DB provider
  - [ ] `AddSekibanDcbDynamoDb()` extension method
  - [ ] Configuration options parity
  - [ ] Error handling patterns

- [ ] Documentation for DynamoDB setup and configuration
  - [ ] Package README
  - [ ] LocalStack setup guide
  - [ ] AWS IAM policy example
  - [ ] Configuration examples

- [ ] S3 offload for large projection states
  - [ ] `IBlobStorageSnapshotAccessor` implementation
  - [ ] Automatic offload when > threshold
  - [ ] LocalStack S3 support

- [ ] Sample project
  - [ ] DcbOrleansDynamoDB.AppHost
  - [ ] Working example with LocalStack

- [ ] Tests
  - [ ] Unit tests
  - [ ] Integration tests with LocalStack
