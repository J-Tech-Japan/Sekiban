# Review: Sekiban DCB Multi-Tenant ServiceId Design & Improvement

## Overview

The design documents provide a comprehensive approach to implementing multi-tenant isolation for Sekiban DCB storage through ServiceId abstraction. This review covers both the base design and the improvement recommendations.

---

## Strengths

### Architecture
- **Clean abstraction**: `IServiceIdProvider` interface enables pluggable tenant resolution (default, fixed, JWT)
- **Domain model preserved**: ServiceId remains storage-only, avoiding pollution of `Event` and `Tag` entities
- **Backwards compatibility**: Single-tenant deployments continue working without code changes
- **Factory pattern for Orleans**: `IEventStoreFactory` enables grain-scoped store creation

### Cosmos DB Design
- **Composite partition key strategy**: `"{serviceId}|{key}"` avoids hot partitions for the default tenant
- **Explicit routing**: Recommendation to limit legacy containers to `default` tenant only prevents mixing concerns
- **Parameterization**: Mandating `QueryDefinition` over string interpolation prevents injection risks

### Postgres Design
- **Online schema evolution**: Additive changes with default values minimize downtime
- **Constraint awareness**: Improvement doc correctly identifies need to include `service_id` in UNIQUE/PK constraints
- **Optional RLS**: Row-Level Security available for high-risk SaaS scenarios without forcing overhead on all users

---

## Critical Issues & Recommendations

### 1. ServiceId Validation (HIGH PRIORITY)

**Gap**: Base design acknowledges ServiceId format as an "Open Question" but doesn't mandate validation.

**Recommendation**: Adopt improvement proposal's validation rules immediately:
```
Format: ^[a-z0-9-]{1,64}$
- Characters: a-z, 0-9, hyphen only
- Length: 1-64 characters
- Forbidden: |, /, whitespace, control characters
```

**Rationale**: Without strict validation, the composite partition key `"{serviceId}|{key}"` risks collision if ServiceId contains the delimiter.

### 2. Default ServiceId Fallback Risk (HIGH PRIORITY)

**Gap**: Base design's "Combined (HTTP + Orleans)" pattern falls back to `DefaultServiceIdProvider` when HTTP context is absent.

**Risk**: Background jobs or misconfigured Orleans grains could inadvertently write to `default` tenant.

**Recommendation**: Implement `MissingServiceIdProvider` as suggested in improvement doc:
```csharp
public sealed class MissingServiceIdProvider : IServiceIdProvider
{
    public string GetCurrentServiceId() => throw new InvalidOperationException(
        "ServiceId is required in non-HTTP context");
}
```

### 3. Cosmos DB Migration Complexity (MEDIUM PRIORITY)

**Gap**: Four migration options provided but no clear default recommendation.

**Recommendation**: Standardize on "Side-by-side (new SaaS tenants)" approach:
- `default` tenant → legacy containers (backward compatibility)
- All other tenants → v2 containers with `/pk` partition key

**Implementation**: Create `ICosmosContainerResolver` interface to route ServiceId to appropriate container names.

### 4. Query Correctness Verification (MEDIUM PRIORITY)

**Gap**: Every query must include ServiceId filter; missing filters cause data leakage.

**Mitigation in design**: Defense-in-depth checks (verify ServiceId on read).

**Additional recommendation**: 
- Add ServiceId predicate enforcement at the repository layer
- Consider code analyzer or unit test that verifies all queries include ServiceId filter
- Integration tests must verify cross-tenant data isolation

### 5. Postgres Constraint Updates (MEDIUM PRIORITY)

**Gap**: Base design mentions indexes but doesn't emphasize UNIQUE/PK constraint changes.

**Required changes**:
- `events`: `PRIMARY KEY (service_id, id)` or keep `id` unique with composite index
- `tags`: `UNIQUE (service_id, tag, sortable_unique_id)`
- `multi_projection_states`: `UNIQUE (service_id, projector_name)`

**Migration order**: Update constraints BEFORE deploying application changes to prevent duplicate key errors.

---

## Minor Suggestions

### Documentation
- Add concrete code example for grain key parsing: `"{serviceId}/{entityId}"`
- Document performance implications: composite indexes add write overhead but prevent cross-tenant leaks

### Testing
- Add test case: ServiceId with special characters (should fail validation)
- Add test case: Verify Orleans grain using `FixedServiceIdProvider` cannot access other tenants' data

### Operational
- Provide migration script template for Cosmos DB data copy
- Document monitoring query to detect cross-tenant access attempts (Postgres RLS log analysis)

---

## Implementation Priority

| Priority | Item | Effort | Impact |
|----------|------|--------|--------|
| P0 | ServiceId validation | Small | Prevents data corruption |
| P0 | Remove default fallback | Small | Prevents accidental writes |
| P1 | Cosmos container routing | Medium | Enables safe migration |
| P1 | Postgres constraint updates | Medium | Prevents tenant collisions |
| P2 | Query verification tests | Medium | Catches developer errors |
| P2 | Migration tooling | Large | Operational readiness |

---

## Summary

The design provides a solid foundation for multi-tenant isolation. The improvement document addresses the most critical gaps (validation, fallback behavior, routing clarity). 

**Key action items**:
1. Implement strict ServiceId validation before any storage operations
2. Replace implicit default fallback with explicit exception-throwing provider
3. Build container routing layer for Cosmos DB to enforce legacy/v2 separation
4. Update Postgres constraints to include `service_id` before application deployment

With these changes, the design achieves both safety (preventing data leaks and corruption) and practicality (supporting gradual migration without breaking existing deployments).
