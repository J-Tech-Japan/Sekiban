# Sekiban DCB Multi-Tenant ServiceId Design

## Status
Draft

## Purpose
Enable multi-tenant (service/tenant) isolation for Sekiban DCB storage while keeping existing single-tenant deployments working. The design adds a ServiceId abstraction and scopes storage operations by tenant, primarily for SekibanAsAService.

## Scope
- DCB Core: add ServiceId provider and factory interfaces.
- Cosmos DB provider: add ServiceId-aware partition key, models, queries, and container configuration.
- Postgres provider: add ServiceId columns, indexes, and query filters.
- DI patterns for single-tenant, HTTP API multi-tenant, and Orleans grain usage.
- Migration strategy for Cosmos DB and Postgres.

## Non-Goals
- No change to domain objects (`Event`, `Tag`) to carry ServiceId.
- No new authentication/authorization framework design (only ServiceId extraction point).
- No changes to non-DCB packages (Pure, TS).

## Terminology
- ServiceId: tenant identifier used to isolate data.
- Tenant: a service/customer in SekibanAsAService.
- Composite partition key (PK): `"{serviceId}|{originalKey}"`.

## Requirements
1. All storage reads/writes must be scoped to the current ServiceId.
2. Existing single-tenant deployments must keep working without code changes.
3. Orleans grains must be able to create ServiceId-scoped stores from grain keys.
4. Cosmos DB partition keys must avoid hot partitions for the default tenant.
5. ServiceId must be validated and normalized before use.

## High-Level Design
### 1) ServiceId Provider (Core)
Introduce a small interface in `Sekiban.Dcb.Core/ServiceId`:

```csharp
public interface IServiceIdProvider
{
    string GetCurrentServiceId(); // never null/empty
}
```

Implementations:
- `DefaultServiceIdProvider`: returns `"default"` (single-tenant). Keep a public constant `DefaultServiceId = "default"` for reuse.
- `FixedServiceIdProvider`: returns a fixed ServiceId (used by grains).
- `JwtServiceIdProvider`: extracts ServiceId from a JWT claim via `IHttpContextAccessor`. Claim type should be configurable (default: `"service_id"`).
- `RequiredServiceIdProvider`: throws `InvalidOperationException` when ServiceId is missing (use for non-HTTP contexts where explicit ServiceId is required).

Notes:
- Multi-tenant usage should register `IServiceIdProvider` as scoped.
- The provider must throw on null/empty ServiceId.
- Providers must validate and normalize ServiceId before returning.

#### 1.1 ServiceId Validation (Resolved)
Adopt strict validation to protect composite PK and query safety.

**Rules**:
- Pattern: `^[a-z0-9-]{1,64}$`
- Normalize: lowercase
- Forbidden: `|`, `/`, whitespace, control characters

**Rationale**:
- Prevents composite PK collisions with `"{serviceId}|{key}"`.
- Ensures safe routing and logging.

### 2) Event Store Factory (Core)
Grains need a factory to create ServiceId-scoped stores:

```csharp
public interface IEventStoreFactory
{
    IEventStore CreateForService(string serviceId);
}

public interface IMultiProjectionStateStoreFactory
{
    IMultiProjectionStateStore CreateForService(string serviceId);
}
```

### 3) Cosmos DB Changes
#### 3.1 Models
Add `pk` and `serviceId` to all Cosmos documents:
- `CosmosEvent`: `pk = "{serviceId}|{eventId}"`
- `CosmosTag`: `pk = "{serviceId}|{tag}"`
- `CosmosMultiProjectionState`: `pk = "{serviceId}|{partitionKey}"`

The domain objects remain unchanged. ServiceId is storage-only.
Keep existing `partitionKey` fields where present for compatibility with current logic, but use `pk` for the actual Cosmos partition key.

#### 3.2 Partition Key Strategy
Change all Cosmos containers to use `/pk` as the partition key path.

Container PK mapping:
- events: `/pk` with `"{serviceId}|{eventId}"`
- tags: `/pk` with `"{serviceId}|{tag}"`
- multiProjectionStates: `/pk` with `"{serviceId}|MultiProjectionState_{projectorName}"`

Rationale:
- Avoid hierarchical PK with a fixed first key (`serviceId`) that would cause hot partitions for `default`.
- Allow cross-tenant queries via WHERE when needed (admin use only).

#### 3.3 CosmosDbContext
Update container creation to use `/pk` and add composite indexes:
- events: index on `serviceId` + `sortableUniqueId`
- tags: index on `serviceId` + `tag` + `sortableUniqueId`

Add options to `CosmosDbEventStoreOptions`:
- `EventsContainerName`, `TagsContainerName`, `MultiProjectionStatesContainerName`
- `UseLegacyPartitionKeyPaths` (default: false)
  - When true, keep legacy PK paths (`/id`, `/tag`, `/partitionKey`) for existing containers.
  - Multi-tenant usage: legacy paths are allowed only for `serviceId == "default"`.

#### 3.4 CosmosDbEventStore
Inject `IServiceIdProvider` and apply ServiceId everywhere:
- ReadAllEventsAsync: filter by `serviceId`.
- ReadEventAsync: partition key = `"{serviceId}|{eventId}"` and verify ServiceId.
- ReadEventsByTagAsync: use `"{serviceId}|{tag}"` PK for tag query, then read events by `"{serviceId}|{eventId}"` PK.
- WriteEventsAsync: include ServiceId in CosmosEvent and PK.
- WriteTagsWithBatchAsync: group by ServiceId-aware tag PK and include ServiceId in CosmosTag.
- Tag-related queries (`GetAllTagsAsync`, `GetLatestTagAsync`, `TagExistsAsync`) must filter by ServiceId.
- Prefer `QueryDefinition` parameters (not string interpolation) to avoid escaping issues.

#### 3.5 CosmosDbMultiProjectionStateStore
Apply `IServiceIdProvider` and composite PK for multi-projection state reads/writes:
- `pk = "{serviceId}|{partitionKey}"` for storage.
- Filter reads by `serviceId` where queries exist.

#### 3.6 Cosmos Container Routing (Resolved)
Adopt explicit routing to prevent legacy/v2 mixing.

**Rule**:
- `serviceId == "default"` → legacy containers (old PK paths)
- All other ServiceIds → v2 containers (`/pk`)

**Interface**:
```csharp
public interface ICosmosContainerResolver
{
    string ResolveEventsContainer(string serviceId);
    string ResolveTagsContainer(string serviceId);
    string ResolveStatesContainer(string serviceId);
}
```

#### 3.7 Cosmos DB Factory Implementations
Add factory types similar to:
- `CosmosDbEventStoreFactory`
- `CosmosDbMultiProjectionStateStoreFactory`

Each factory constructs a store with a `FixedServiceIdProvider`.

### 4) Postgres Changes
Postgres supports online schema evolution; use columns + indexes.

#### 4.1 Schema
Add `service_id` columns with default `"default"`:
- `events`: add `service_id`, index on `service_id`, and composite index on `(service_id, sortable_unique_id)`
- `tags`: add `service_id`, index on `(service_id, tag)` and `(service_id, tag, sortable_unique_id)`
- `multi_projection_states`: add `service_id`, index on `(service_id, projector_name)`

Update UNIQUE/PK constraints to include `service_id` where needed:
- `events`: consider `PRIMARY KEY (service_id, id)` if `id` is only unique within a tenant.
- `tags`: `UNIQUE (service_id, tag, sortable_unique_id)`
- `multi_projection_states`: `UNIQUE (service_id, projector_name)`
 
Migration order: apply constraint changes **before** deploying application changes to avoid cross-tenant collisions during rollout.

#### 4.2 Query Changes
Inject `IServiceIdProvider` and add `service_id` filters to all reads and writes.

#### 4.3 Factory
Provide `PostgresEventStoreFactory` to construct ServiceId-scoped stores (for grains).

#### 4.4 Optional Row-Level Security
Optionally enable RLS to enforce tenant isolation at the DB level:
- `USING (service_id = current_setting('app.service_id'))`

### 5) DI Registration Patterns
#### Single-Tenant (default)
- Register `DefaultServiceIdProvider` as singleton.
- Register `IEventStore` and `IMultiProjectionStateStore` as singleton.
- Suggested extension method name: `AddSekibanDcbCosmosDb`.

#### Multi-Tenant HTTP API
- Register `IHttpContextAccessor`.
- Register `JwtServiceIdProvider` as scoped.
- Register stores as scoped (new instance per request).
- Suggested extension method name: `AddSekibanDcbCosmosDbMultiTenant` with `serviceIdClaimType` parameter.

#### Orleans Grains
- Register `IEventStoreFactory` and `IMultiProjectionStateStoreFactory` as singleton.
- Grains extract ServiceId from grain key and create a fixed store at activation.
- Suggested extension method name: `AddSekibanDcbCosmosDbWithFactories`.

#### Combined (HTTP + Orleans)
- Factories as singleton.
- `IServiceIdProvider` as scoped: use JWT if HTTP context exists, else use `RequiredServiceIdProvider` to force explicit ServiceId in non-HTTP contexts.
- Stores as scoped.
- Suggested extension method name: `AddSekibanDcbCosmosDbFull`.

## Migration Strategy
### Cosmos DB Constraints
Cosmos DB does not allow changing partition key paths on existing containers. Migration is required.

### Options
1) New containers with `/pk` (recommended for SaaS)
- Create `events_v2`, `tags_v2`, `multiProjectionStates_v2`.
- Migrate data from old containers.
- Switch app config to new container names.

2) New database
- Create a new DB with the new containers.
- Update connection strings.

3) Dual-write (zero-downtime)
- Temporarily write to both old and new containers.
- Read from old during backfill; switch to new after verification.

4) Side-by-side (recommended for new SaaS tenants)
- Keep legacy containers for `default` tenant.
- Use new containers for new tenants.
  - Achieve with `UseLegacyPartitionKeyPaths = true` for legacy paths or by using container name overrides for v2 containers.

### Recommended Default
Adopt **side-by-side** for SaaS:
- `default` → legacy containers (backward compatibility)
- others → v2 containers (`/pk`)
This avoids breaking existing deployments while enabling safe multi-tenant rollout.

### Postgres
Schema changes are additive and online. Existing rows get `service_id = 'default'` automatically.

## Benefits
- Strong tenant isolation at the storage layer without changing domain models.
- Backwards compatibility for existing single-tenant deployments.
- Cosmos DB partition key design avoids hot partitions for the default tenant.
- Clear support for Orleans grain activation and ServiceId scoping.
- Extensible to multiple storage backends.

## Risks and Trade-offs
1) Cosmos migration complexity
- Requires new containers or database; data copy is operationally heavy.
- Risk of mismatched data if migration is partial or fails mid-way.

2) Query correctness risk
- Every query must include ServiceId filter; missing filters can leak data.
- Mitigation: defense-in-depth checks (e.g., read item verifies ServiceId).

3) Partition key changes
- Old containers cannot be reused with new `/pk` path.
- Side-by-side increases operational footprint.

4) Performance considerations
- Additional filtering by ServiceId adds query predicates and may affect RU usage.
- Composite indexes should mitigate common queries.

5) DI lifetime complexity
- Incorrect DI scope (singleton vs scoped) can cause ServiceId leakage.

6) Optional RLS (Postgres)
- Improves isolation but adds operational and performance overhead.
 
7) Cosmos query parameterization
- String interpolation in SQL queries risks escaping issues if ServiceId contains special characters.
- Mitigation: use `QueryDefinition` parameters for ServiceId and sortable IDs.

8) Observability gap
- Without ServiceId in logs/metrics/traces, tenant-specific incidents are hard to triage.
- Mitigation: include ServiceId in structured logs and metrics labels (where safe).

## Rollout Plan (Suggested)
1) Add Core interfaces and default implementations.
2) Update Cosmos models and context with `/pk` support and options.
3) Update Cosmos event store queries and tag writes with ServiceId.
4) Add factories and DI patterns.
5) Update Postgres schema and queries.
6) Provide migration tools or scripts for Cosmos.
7) Deploy to staging with a sample multi-tenant workload.
8) Add observability: include ServiceId in logs/metrics/traces.

## Testing Plan
- Unit tests for ServiceId providers (default, fixed, JWT, required).
- ServiceId validation tests: accept valid, reject invalid, normalize to lowercase.
- Container routing tests: `default` → legacy, others → v2.
- Integration tests per provider:
  - Writes/reads scoped to ServiceId.
  - Cross-tenant data isolation.
  - Tag queries scoped to ServiceId.
  - Multi-projection state isolation.
- Migration tests:
  - Data copied from legacy containers to v2 containers.
  - Counts and spot-checks match.
  - Postgres constraint migration with existing data.

### Query Correctness Guardrails (Recommended)
- Add unit tests or static checks to ensure all queries include ServiceId filters.
- Consider a thin repository layer that enforces ServiceId predicates centrally.

## Open Questions
1) Whether to enforce ServiceId in all tag list queries via SDK LINQ vs SQL.
2) Whether to add explicit admin-only cross-tenant query APIs (recommended to be separate from `IEventStore`).
3) Whether to enable Postgres RLS by default or keep it optional.

## Implementation Priority
- P0: ServiceId validation and normalization.
- P0: Replace default fallback with `RequiredServiceIdProvider` for non-HTTP contexts.
- P1: Cosmos container routing via `ICosmosContainerResolver`.
- P1: Postgres constraint updates including `service_id`.
- P2: Query correctness guardrails and migration tooling.

## Implementation Checklist (Phased)
### Phase 1: Core Interfaces
- Add `IServiceIdProvider` and implementations (`Default`, `Fixed`, `Jwt`, `Required`).
- Add `IEventStoreFactory` and `IMultiProjectionStateStoreFactory`.
- Add ServiceId validation/normalization.

### Phase 2: Cosmos DB
- Add `pk`/`serviceId` to Cosmos models.
- Implement `ICosmosContainerResolver` and routing.
- Update `CosmosDbEventStore` and `CosmosDbMultiProjectionStateStore` to use ServiceId and parameterized queries.
- Add composite indexes and options for container naming/legacy paths.

### Phase 3: Postgres
- Add `service_id` column and indexes.
- Update UNIQUE/PK constraints to include `service_id`.
- Update all queries with ServiceId filtering.
- Implement `PostgresEventStoreFactory`.

### Phase 4: DI and Tests
- Add DI extension methods per deployment pattern.
- Add unit/integration tests described above.
- Add migration documentation and scripts.

## Appendix: Example Grain Pattern
- Grain key contains `"{serviceId}/{entityId}"`.
- `OnActivateAsync`: extract ServiceId from the prefix before `/`.
- If no `/` exists, fallback to `DefaultServiceIdProvider.DefaultServiceId` for single-tenant compatibility.

## Appendix: Example Options
```csharp
public class CosmosDbEventStoreOptions
{
    public string EventsContainerName { get; set; } = "events";
    public string TagsContainerName { get; set; } = "tags";
    public string MultiProjectionStatesContainerName { get; set; } = "multiProjectionStates";
    public bool UseLegacyPartitionKeyPaths { get; set; } = false;
}
```
