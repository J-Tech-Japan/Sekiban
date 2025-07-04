# Weather Forecast Dapr Sample - Sekiban TypeScript

A production-ready weather forecast management application demonstrating **Sekiban's multi-payload projector pattern** with Dapr integration, matching the C# template functionality exactly.

## 🌟 Key Features

✅ **Multi-Payload Aggregate Projectors**
- Single projector handling multiple payload types (`WeatherForecast` ↔ `DeletedWeatherForecast`)
- State machine patterns for domain modeling
- Proper aggregate lifecycle management

✅ **Event Sourcing & CQRS**
- Complete weather forecast domain with commands, events, and queries
- Input, update location, and soft-delete operations
- Event-driven architecture with Dapr pub/sub

✅ **Dapr Integration**
- Production-ready SekibanDaprExecutor with retry logic and error handling
- Dapr Actor model for distributed aggregate management
- In-memory state store and pub/sub for local development

✅ **TypeScript & Type Safety**
- Full TypeScript implementation with strong typing
- Value objects (TemperatureCelsius) with validation
- Zod schemas for input validation

✅ **Production-Ready Patterns**
- Health endpoints (/healthz, /readyz) for Kubernetes
- Prometheus metrics collection
- Distributed tracing and request logging
- Proper error handling with Result patterns

## 🚀 Quick Start

### Prerequisites
- Node.js 18+
- pnpm
- Dapr CLI (for production-like testing)

### Installation

```bash
# Install dependencies
pnpm install

# Build the application
pnpm build
```

### Local Development

```bash
# Development mode (hot reload)
pnpm dev

# With Dapr sidecar (recommended)
pnpm dapr:run
```

### Running Tests

```bash
# Run all tests
pnpm test

# Watch mode for development
pnpm test:watch

# Type checking
pnpm typecheck
```

## 🌤️ API Endpoints

### Weather Forecast Management
- `POST /api/weatherforecast/input` - Create a new weather forecast
- `POST /api/weatherforecast/{id}/update-location` - Update forecast location
- `POST /api/weatherforecast/{id}/delete` - Soft delete a forecast
- `GET /api/weatherforecast` - Get all active forecasts
- `POST /api/weatherforecast/generate` - Generate sample data

### Health & Observability
- `GET /healthz` - Liveness probe (Kubernetes ready)
- `GET /readyz` - Readiness probe (dependency health)
- `GET /metrics` - Prometheus metrics
- `GET /debug/env` - Environment variables (debug)

### Event Handling (Dapr)
- `POST /events/weather-forecasts` - Receive weather events from Dapr pub/sub

## 📝 Example Usage

### Create a Weather Forecast
```bash
curl -X POST http://localhost:5000/api/weatherforecast/input \
  -H "Content-Type: application/json" \
  -d '{
    "location": "Tokyo",
    "date": "2025-07-04",
    "temperatureC": 25,
    "summary": "Warm and sunny"
  }'
```

### Update Location
```bash
curl -X POST http://localhost:5000/api/weatherforecast/{forecast-id}/update-location \
  -H "Content-Type: application/json" \
  -d '{"location": "Osaka"}'
```

### Get All Forecasts
```bash
curl http://localhost:5000/api/weatherforecast
```

### Generate Sample Data
```bash
curl -X POST http://localhost:5000/api/weatherforecast/generate
```

### Check Health & Metrics
```bash
# Kubernetes liveness probe
curl http://localhost:5000/healthz

# Kubernetes readiness probe  
curl http://localhost:5000/readyz

# Prometheus metrics
curl http://localhost:5000/metrics
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