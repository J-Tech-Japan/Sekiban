# Sekiban Dapr Aspire Template

<!-- SEKIBAN_PURE_POLICY_START -->
> **Sekiban.Pure support policy / サポート方針**
>
> Sekiban.Pure is in maintenance mode (no new features; .NET 9). We plan to complete its lifecycle with .NET 9 (EOL Nov 10, 2026) — but if you run it in production, we will bring it to .NET 10/11 as a pure compatibility migration, no features (we shipped exactly this for Sekiban(Core) 0.25.0: unchanged public API, bidirectional data compatibility). Tell us in [#1169](https://github.com/J-Tech-Japan/Sekiban/issues/1169). For new projects we recommend Sekiban DCB.
>
> Sekiban.Pure は現在メンテナンスモードです（機能追加なし・.NET 9 対応）。.NET 9 の EOL（2026-11-10）でライフサイクルを完了する予定ですが、本番利用中の方がいれば .NET 10/11 への対応を機能追加なしの互換移行として行います（Sekiban(Core) 0.25.0 で同方式を実施済み: 公開 API 無変更・保存データ双方向互換）。必要な方は [#1169](https://github.com/J-Tech-Japan/Sekiban/issues/1169) でお知らせください。新規プロジェクトには Sekiban DCB を推奨します。
<!-- SEKIBAN_PURE_POLICY_END -->


This template creates a new Sekiban application using Dapr for distributed event sourcing and CQRS with .NET Aspire.

## Prerequisites

- .NET 9.0 SDK
- Dapr CLI installed
- Docker Desktop (for Aspire and Dapr)

## Getting Started

1. Create a new project:
```bash
dotnet new sekiban-dapr-aspire -n MyProject
```

2. Navigate to the project directory:
```bash
cd MyProject
```

3. Restore dependencies:
```bash
dotnet restore
```

4. Run the Aspire AppHost:
```bash
dotnet run --project DaprSekiban.AppHost
```

This will start:
- The Aspire dashboard
- Dapr sidecars
- PostgreSQL database
- API service
- Blazor web frontend

## Project Structure

- **DaprSekiban.ApiService** - ASP.NET Core Web API with Dapr integration
- **DaprSekiban.AppHost** - Aspire orchestration host
- **DaprSekiban.Domain** - Domain models, events, commands, and projectors
- **DaprSekiban.ServiceDefaults** - Shared service configuration
- **DaprSekiban.Web** - Blazor Server web application
- **DaprSekiban.Unit** - Unit tests (optional)
- **dapr-components** - Dapr component configuration files

## Features

- Event Sourcing with Sekiban.Pure.Dapr
- CQRS pattern implementation
- Dapr state store for event storage
- Dapr pub/sub for event distribution
- Dapr actors for aggregate grains
- PostgreSQL support (can be switched to Cosmos DB)
- Aspire orchestration and observability
- Sample domain with User and WeatherForecast aggregates

## Configuration

### Dapr Components

The Dapr components are configured in the `dapr-components` directory:
- `statestore.yaml` - State store configuration
- `pubsub.yaml` - Pub/sub configuration
- `config.yaml` - Dapr configuration

### Database

By default, the template uses PostgreSQL. To switch to Cosmos DB:

1. Update `appsettings.json` in ApiService:
```json
{
  "Sekiban": {
    "Database": "Cosmos",
    "CosmosDb": {
      "ConnectionString": "your-cosmos-connection-string",
      "DatabaseId": "your-database-id",
      "ContainerId": "your-container-id"
    }
  }
}
```

2. Remove the PostgreSQL reference from AppHost if not needed.

## Development

### Adding New Aggregates

1. Create aggregate files in the Domain project under `Aggregates/YourAggregate/`
2. Define commands, events, payloads, queries, and projector
3. Register in the JSON context file
4. Add API endpoints in ApiService

### Running Tests

```bash
dotnet test
```

## Deployment

The template includes basic infrastructure setup. For production deployment:
1. Configure proper Dapr components for your environment
2. Set up appropriate state stores and pub/sub brokers
3. Configure authentication and authorization
4. Set up monitoring and logging

## Learn More

- [Sekiban Documentation](https://github.com/J-Tech-Japan/Sekiban)
- [Dapr Documentation](https://docs.dapr.io)
- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire)