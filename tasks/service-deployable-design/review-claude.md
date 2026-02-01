# Claude Review: Sekiban DCB Multi-Tenant ServiceId Design

## Executive Summary

This review evaluates the multi-tenant ServiceId design for Sekiban DCB storage. The design is well-structured and addresses the core requirements for SaaS deployment. The improvement document provides valuable refinements that should be incorporated. Overall assessment: **Approved with required changes**.

---

## Document Quality Assessment

### Base Design (`sekiban-dcb-multitenant-serviceid-design.md`)

**Strengths**:
- Clear scope definition with explicit non-goals
- Comprehensive coverage of both Cosmos DB and Postgres providers
- Well-considered DI registration patterns for different deployment scenarios
- Risk/trade-off section demonstrates mature engineering thinking

**Areas for Improvement**:
- Open questions should be resolved before implementation begins
- Migration strategy needs a clear recommendation rather than four options

### Improvement Document (`sekiban-dcb-multitenant-serviceid-improvement.md`)

**Strengths**:
- Addresses critical gaps in the base design
- Provides concrete validation rules and code examples
- Clear recommendations with rationale

**Note**: Written in Japanese, which is acceptable for internal discussion but should be consolidated into English for the final design per project guidelines.

---

## Critical Review Points

### 1. ServiceId Specification - MUST RESOLVE

**Issue**: ServiceId format is listed as an open question but is foundational to the design.

**Required Decision**:
```
Pattern: ^[a-z0-9-]{1,64}$
Allowed: lowercase letters, digits, hyphens
Forbidden: | / (space) (control chars)
```

**Implementation Requirement**: `IServiceIdProvider.GetCurrentServiceId()` must validate and normalize (lowercase) input before returning.

**Failure Mode**: Without validation, a ServiceId containing `|` would corrupt the composite partition key `"{serviceId}|{key}"`.

### 2. Default Fallback Behavior - MUST CHANGE

**Current Design**:
```
HTTP + Orleans → Fallback to DefaultServiceIdProvider when HTTP context missing
```

**Problem**: Silent fallback enables accidental writes to `default` tenant from:
- Background jobs
- Message handlers
- Misconfigured Orleans grains
- Timer callbacks

**Required Change**:
```csharp
// Replace DefaultServiceIdProvider fallback with:
public sealed class RequiredServiceIdProvider : IServiceIdProvider
{
    public string GetCurrentServiceId() =>
        throw new InvalidOperationException(
            "ServiceId must be explicitly provided. " +
            "Use FixedServiceIdProvider for background operations.");
}
```

### 3. Cosmos DB Container Routing - NEEDS CLARIFICATION

**Current Design**: Four migration options without clear recommendation.

**Required Clarification**:
| Tenant | Container Set | Rationale |
|--------|---------------|-----------|
| `default` | Legacy (`/id`, `/tag`, `/partitionKey`) | Backward compatibility |
| All others | v2 (`/pk`) | Clean multi-tenant design |

**Implementation**: Add explicit routing interface:
```csharp
public interface ICosmosContainerResolver
{
    string ResolveEventsContainer(string serviceId);
    string ResolveTagsContainer(string serviceId);
    string ResolveStatesContainer(string serviceId);
}
```

### 4. Postgres Constraint Gaps - MUST ADDRESS

**Base Design Gap**: Focuses on indexes but omits UNIQUE/PRIMARY KEY constraint updates.

**Required Schema Changes**:
```sql
-- events: ensure tenant isolation
ALTER TABLE events DROP CONSTRAINT events_pkey;
ALTER TABLE events ADD PRIMARY KEY (service_id, id);

-- tags: prevent cross-tenant collision
ALTER TABLE tags DROP CONSTRAINT IF EXISTS tags_unique;
ALTER TABLE tags ADD CONSTRAINT tags_unique
    UNIQUE (service_id, tag, sortable_unique_id);

-- multi_projection_states: tenant-scoped uniqueness
ALTER TABLE multi_projection_states DROP CONSTRAINT IF EXISTS mps_unique;
ALTER TABLE multi_projection_states ADD CONSTRAINT mps_unique
    UNIQUE (service_id, projector_name);
```

**Migration Order**: Constraint changes must be applied BEFORE deploying application changes to prevent duplicate key violations.

---

## Design Validation

### Scenario: Single-Tenant Deployment
- Uses `DefaultServiceIdProvider` returning `"default"`
- No code changes required for existing users
- **Verdict**: ✅ Supported

### Scenario: Multi-Tenant HTTP API
- Uses `JwtServiceIdProvider` scoped per request
- ServiceId extracted from JWT claim
- **Verdict**: ✅ Supported (with validation requirement)

### Scenario: Orleans Grain with ServiceId
- Grain key format: `"{serviceId}/{entityId}"`
- Factory creates `FixedServiceIdProvider` at activation
- **Verdict**: ✅ Supported

### Scenario: Background Job Processing
- **Current Design**: Falls back to `default`
- **After Fix**: Throws exception, requires explicit ServiceId
- **Verdict**: ⚠️ Requires implementation change

### Scenario: Cross-Tenant Admin Query
- Listed as open question
- **Recommendation**: Create separate admin-only interface, not exposed via standard `IEventStore`
- **Verdict**: ⏳ Deferred (acceptable)

---

## Testing Requirements

### Unit Tests (Required Before Merge)
1. ServiceId validation - accept valid, reject invalid patterns
2. ServiceId normalization - uppercase input converted to lowercase
3. `RequiredServiceIdProvider` throws `InvalidOperationException`
4. Container routing - `default` → legacy, others → v2

### Integration Tests (Required Before Release)
1. Write event with ServiceId A, verify not readable with ServiceId B
2. Tag query isolation between tenants
3. Multi-projection state isolation
4. Orleans grain ServiceId extraction from compound key

### Migration Tests (Required Before Production)
1. Cosmos: Data copied correctly with new partition keys
2. Cosmos: Legacy container still works for `default` tenant
3. Postgres: Constraint migration successful with existing data
4. Postgres: `service_id='default'` applied to existing rows

---

## Implementation Checklist

### Phase 1: Core Interfaces (Week 1)
- [ ] Add `IServiceIdProvider` to `Sekiban.Dcb.Core`
- [ ] Implement `DefaultServiceIdProvider` with validation
- [ ] Implement `FixedServiceIdProvider` with validation
- [ ] Implement `RequiredServiceIdProvider` (throws)
- [ ] Add `IEventStoreFactory` and `IMultiProjectionStateStoreFactory`

### Phase 2: Cosmos DB (Week 2)
- [ ] Add `pk`, `serviceId` fields to Cosmos models
- [ ] Implement `ICosmosContainerResolver`
- [ ] Update `CosmosDbEventStore` with ServiceId filtering
- [ ] Update `CosmosDbMultiProjectionStateStore`
- [ ] Implement `CosmosDbEventStoreFactory`
- [ ] Update container creation with composite indexes

### Phase 3: Postgres (Week 2-3)
- [ ] Add `service_id` column with migration script
- [ ] Update UNIQUE/PK constraints
- [ ] Update all queries with ServiceId filter
- [ ] Implement `PostgresEventStoreFactory`
- [ ] (Optional) Add RLS policy

### Phase 4: DI and Testing (Week 3)
- [ ] Add extension methods per deployment pattern
- [ ] Implement `JwtServiceIdProvider`
- [ ] Write unit tests
- [ ] Write integration tests
- [ ] Create migration documentation

---

## Comparison with OpenCode Review

The existing `review-opencode.md` covers similar ground. Key differences in this review:

| Topic | OpenCode Review | This Review |
|-------|-----------------|-------------|
| ServiceId validation | Recommends adoption | Marks as MUST RESOLVE |
| Fallback behavior | `MissingServiceIdProvider` | `RequiredServiceIdProvider` (same concept) |
| Container routing | `ICosmosContainerResolver` | Same recommendation |
| Postgres constraints | Lists required changes | Provides SQL examples |
| Implementation priority | P0/P1/P2 table | Phase-based checklist |

**Alignment**: Both reviews agree on critical issues. This review adds concrete SQL examples and a phased implementation plan.

---

## Final Recommendations

1. **Merge the improvement proposals** into the base design document
2. **Resolve open questions** before implementation:
   - ServiceId format: Use `^[a-z0-9-]{1,64}$`
   - Admin cross-tenant API: Defer to separate design
   - Postgres RLS: Keep optional
3. **Implement in phases** to enable incremental validation
4. **Consolidate documentation** into English per project guidelines
5. **Add ServiceId to observability**: Logs, metrics, and traces should include ServiceId for debugging tenant-specific issues

---

## Verdict

**Approved with Required Changes**

The design is sound and ready for implementation after addressing:
1. ✅ ServiceId validation rules (adopt improvement proposal)
2. ✅ Default fallback replacement (adopt improvement proposal)
3. ✅ Container routing clarification (add interface)
4. ✅ Postgres constraint updates (add to schema migration)

No fundamental architectural changes required. Proceed to implementation with the noted refinements.
