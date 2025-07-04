# Sekiban TypeScript Dapr Sample

This sample demonstrates how to use Sekiban with Dapr for distributed event sourcing in TypeScript, implementing a Weather Forecast domain that matches the C# template functionality.

## Architecture

The sample implements a Weather Forecast domain using:
- **Multi-payload projectors**: One projector handling multiple aggregate states
- **State machine pattern**: WeatherForecast → DeletedWeatherForecast transitions
- **Value objects**: TemperatureCelsius with validation
- **CQRS**: Separate command and query models
- **Dapr integration**: Actors, state management, and pub/sub

## Project Structure

```
dapr-sample/
├── packages/
│   └── domain/          # Domain logic (commands, events, projectors)
├── apps/
│   ├── api/            # Backend API server
│   └── web/            # Frontend application (placeholder)
├── dapr-components/     # Dapr component configurations for Docker
├── dapr-components-local/ # Dapr component configurations for local development
└── dapr-config/        # Dapr configuration files
```

## Prerequisites

- Node.js 20+
- pnpm
- Dapr CLI installed
- Docker and Docker Compose (for full deployment)

## Local Development

### 1. Install dependencies

```bash
pnpm install
```

### 2. Build the packages

```bash
pnpm build
```

### 3. Run tests

```bash
# Run all tests
pnpm test

# Run tests in watch mode
pnpm test:watch
```

### 4. Run with Dapr (local development)

```bash
# Terminal 1: Start the API with Dapr sidecar
cd apps/api
pnpm dapr:run

# The API will be available at:
# - Direct: http://localhost:3000
# - Via Dapr: http://localhost:3500
```

## Docker Deployment

### 1. Build and run with Docker Compose

```bash
# Build and start all services
docker-compose up --build

# Stop all services
docker-compose down
```

This will start:
- PostgreSQL database
- Redis for Dapr state and pub/sub
- Weather Forecast API with Dapr sidecar

## API Endpoints

### Weather Forecast Management

- `POST /api/weatherforecast/input` - Create a new weather forecast
  ```json
  {
    "location": "Tokyo",
    "date": "2024-01-15",
    "temperatureC": 25,
    "summary": "Warm"
  }
  ```

- `POST /api/weatherforecast/:id/update-location` - Update location
  ```json
  {
    "location": "Osaka"
  }
  ```

- `POST /api/weatherforecast/:id/delete` - Soft delete (mark as deleted)
- `POST /api/weatherforecast/:id/remove` - Hard delete (remove from system)
- `GET /api/weatherforecast` - Get all weather forecasts
- `POST /api/weatherforecast/generate` - Generate sample data

### Health and Observability

- `GET /healthz` - Liveness check
- `GET /readyz` - Readiness check
- `GET /metrics` - Prometheus metrics
- `GET /debug/env` - Environment variables (debug)

### Dapr Actor Endpoints

- `GET /dapr/config` - Dapr configuration
- `GET /actors/AggregateActor/health` - Actor health check

## Development Tips

### Running individual services

```bash
# Run only the API server (without Dapr)
cd apps/api
pnpm dev

# Run domain tests
cd packages/domain
pnpm test
```

### Debugging with Dapr Dashboard

```bash
# Start Dapr dashboard
dapr dashboard

# Access at http://localhost:8080
```

### Environment Variables

- `DAPR_HOST` - Dapr sidecar host (default: localhost)
- `DAPR_HTTP_PORT` - Dapr HTTP port (default: 3500)
- `DAPR_GRPC_PORT` - Dapr gRPC port (default: 50001)
- `DAPR_STATE_STORE` - State store component name (default: sekiban-eventstore)
- `DAPR_PUBSUB` - Pub/sub component name (default: sekiban-pubsub)
- `DAPR_EVENT_TOPIC` - Event topic name (default: domain-events)

## Testing

The sample includes comprehensive test suites:

- **Unit tests**: Domain logic, commands, events, projectors
- **Acceptance tests**: Full command/query lifecycle testing
- **Integration tests**: API endpoint testing

Run specific test suites:

```bash
# Domain tests only
cd packages/domain && pnpm test

# API tests only
cd apps/api && pnpm test
```

## Architecture Notes

### Multi-Payload Projector Pattern

The `WeatherForecastProjector` demonstrates Sekiban's powerful pattern where one projector handles multiple aggregate states:

```typescript
type WeatherForecastPayloadUnion = 
  | WeatherForecast 
  | DeletedWeatherForecast;

class WeatherForecastProjector extends AggregateProjector<WeatherForecastPayloadUnion> {
  // Handles state transitions between different payload types
}
```

### State Machine

The weather forecast follows these state transitions:
1. Empty → WeatherForecast (via InputWeatherForecastCommand)
2. WeatherForecast → WeatherForecast (via UpdateLocationCommand)
3. WeatherForecast → DeletedWeatherForecast (via DeleteCommand)
4. Any → Empty (via RemoveCommand)

### Event Sourcing

All state changes are captured as events:
- `WeatherForecastInputted`
- `WeatherForecastLocationUpdated`
- `WeatherForecastDeleted`

These events are stored in the event store and can be replayed to reconstruct aggregate state.

## Troubleshooting

### Dapr sidecar not starting

1. Ensure Dapr is installed: `dapr --version`
2. Initialize Dapr: `dapr init`
3. Check Dapr status: `dapr status`

### Port conflicts

The sample uses these ports:
- 3000: API server
- 3500: Dapr HTTP
- 50001: Dapr gRPC
- 5432: PostgreSQL
- 6379: Redis

Ensure these ports are available or modify the configuration.

### Build errors

1. Clear build artifacts: `pnpm clean`
2. Reinstall dependencies: `pnpm install`
3. Rebuild: `pnpm build`