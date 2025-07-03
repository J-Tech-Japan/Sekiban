# Sekiban TypeScript Dapr Sample

A comprehensive TypeScript sample application demonstrating Sekiban event sourcing with Dapr integration, built using Test-Driven Development (TDD) following Takuto Wada's methodology.

## Features

✅ **Event Sourcing & CQRS**
- User aggregate with commands, events, and projections
- Command/Query separation with proper validation
- Event-driven architecture with pub/sub integration

✅ **Dapr Integration**
- Real-time event streaming with Dapr pub/sub
- In-memory components for local development  
- CloudEvents specification compliance
- Graceful fallback when pub/sub is unavailable

✅ **Production-Ready Patterns**
- Comprehensive test suite (17 passing tests)
- Contract testing with mock Dapr client
- Input validation with Zod
- Proper error handling and HTTP status codes

✅ **Health & Observability**
- Kubernetes-ready health endpoints (/healthz, /readyz)
- Prometheus metrics with counters and histograms
- Distributed tracing context propagation
- Dependency health checking

✅ **Developer Experience**
- Hot-reloading development setup
- Clear separation of concerns
- Dependency injection for testability

✅ **Time-Travel Debugging** (NEW)
- Historical state reconstruction at any point in time
- Event replay with metadata tracking
- Performance-optimized for large event streams
- REST API support with `asOf` query parameter

## Quick Start

### Prerequisites
- Node.js 18+
- pnpm
- Dapr CLI (optional, for production-like testing)

### Installation

```bash
pnpm install
```

### Running Tests

```bash
# Run all tests
pnpm test

# Run only acceptance tests  
pnpm test:acceptance

# Watch mode
pnpm test:watch
```

### Local Development

```bash
# Start PostgreSQL (optional - tests use in-memory)
pnpm docker:up

# Run the API server
pnpm dev:api

# Or with Dapr sidecar
pnpm dapr:start
```

## API Endpoints

### User Management
- `POST /users` - Create a new user
- `GET /users/:id` - Retrieve user by ID
- `GET /users/:id?asOf=2025-07-03T10:30:00.000Z` - Retrieve user state at a specific point in time

### Health & Observability
- `GET /healthz` - Liveness probe (always returns 200 when process is alive)
- `GET /readyz` - Readiness probe (checks dependencies, returns 503 if not ready)
- `GET /metrics` - Prometheus metrics endpoint
- `GET /health` - Legacy health check

### Event Handling (Dapr)
- `POST /events/users` - Receive user events from Dapr pub/sub

## Example Usage

### Create a User
```bash
curl -X POST http://localhost:3000/users \\
  -H "Content-Type: application/json" \\
  -d '{"name": "Alice Johnson", "email": "alice@example.com"}'
```

### Get a User
```bash
curl http://localhost:3000/users/{user-id}
```

### Time-Travel Query
```bash
# Get user state as it was at a specific time
curl "http://localhost:3000/users/{user-id}?asOf=2025-07-03T10:30:00.000Z"
```

### Check Health & Metrics
```bash
# Liveness probe
curl http://localhost:3000/healthz

# Readiness probe
curl http://localhost:3000/readyz

# Prometheus metrics
curl http://localhost:3000/metrics
```

## Architecture

### Domain Layer
```
src/domain/user/
├── commands/           # CreateUserCommand
├── events/            # UserRegisteredEvent  
└── queries/           # GetUserQuery
```

### Infrastructure Layer
```
src/infrastructure/
├── simple-sekiban-executor.ts    # Core event sourcing logic
└── create-sekiban-executor.ts    # Factory function
```

### API Layer
```
src/
├── app.ts             # Express application setup
├── routes/            # HTTP route handlers
├── validators/        # Input validation with Zod
└── middleware/        # Logging and error handling
```

### Dapr Components
```
dapr-components/
├── pubsub.yaml        # In-memory pub/sub component
├── statestore.yaml    # In-memory state store  
├── subscription.yaml  # Event subscription config
└── config.yaml        # Dapr configuration
```

## Event Flow

1. **Command** → `POST /users` with user data
2. **Validation** → Zod schema validation
3. **Event Creation** → `UserRegistered` event generated
4. **Persistence** → Event stored in event store
5. **Projection Update** → User read model updated
6. **Event Publishing** → CloudEvent published to Dapr pub/sub
7. **Event Consumption** → Event received at `/events/users` endpoint

## Testing Strategy

Following modern TDD practices with outside-in approach:

### Acceptance Tests (23 tests)
- **User Sign-up Flow** (4 tests)
  - ✅ Complete user registration and retrieval  
  - ✅ Input validation and error handling
  - ✅ Duplicate email prevention
  - ✅ Non-existent user handling

- **Pub/Sub Integration** (4 tests)  
  - ✅ CloudEvent publishing on user registration
  - ✅ CloudEvent metadata and specification compliance
  - ✅ No publishing on failed registration
  - ✅ Graceful handling of pub/sub failures

- **Health & Observability** (9 tests)
  - ✅ Liveness and readiness endpoints
  - ✅ Prometheus metrics format and collection
  - ✅ User registration counter tracking
  - ✅ HTTP request duration histograms
  - ✅ Dependency health checking
  - ✅ Trace context propagation

- **Time-Travel Debugging** (6 tests) **NEW**
  - ✅ Historical state reconstruction at specific timestamps
  - ✅ Event filtering by time boundaries  
  - ✅ Edge case handling (future timestamps, non-existent entities)
  - ✅ Current state retrieval without time parameters
  - ✅ Performance optimization for large event streams
  - ✅ Replay metadata tracking

### Contract Testing
- Mock Dapr client for reliable, fast feedback
- CloudEvents schema validation
- Message boundary testing

## Development Principles

Built following **Takuto Wada's TDD methodology**:

1. **Outside-in Development** - Start with acceptance tests
2. **Thin Vertical Slices** - Complete features end-to-end  
3. **Red-Green-Refactor** - Fail fast, make it work, make it better
4. **Baby Steps** - Small, focused changes with fast feedback

## Next Steps

This sample demonstrates the foundation. Potential next iterations:

- [ ] WeatherForecast aggregate (following C# template)
- [ ] Real PostgreSQL integration with @sekiban/postgres
- [ ] React frontend with real-time updates
- [ ] Full OpenTelemetry integration (spans, logs)
- [ ] Multi-projection queries and analytics
- [ ] Production deployment with Docker Compose

## Technology Stack

- **Framework**: Express.js + TypeScript
- **Event Sourcing**: Custom Sekiban implementation
- **Validation**: Zod
- **Testing**: Vitest + Supertest  
- **Error Handling**: neverthrow Result pattern
- **Pub/Sub**: Dapr with in-memory broker
- **Observability**: Prometheus metrics, health checks, distributed tracing
- **Development**: Hot-reload with pnpm workspaces

---

*Generated with ❤️ using Test-Driven Development and Claude Code*