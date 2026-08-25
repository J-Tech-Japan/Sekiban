# Sekiban Event Sourcing Templates

<!-- SEKIBAN_PURE_POLICY_START -->
> **Sekiban.Pure support policy / サポート方針**
>
> Sekiban.Pure is in maintenance mode (no new features; .NET 9). We plan to complete its lifecycle with .NET 9 (EOL Nov 10, 2026) — but if you run it in production, we will bring it to .NET 10/11 as a pure compatibility migration, no features (we shipped exactly this for Sekiban(Core) 0.25.0: unchanged public API, bidirectional data compatibility). Tell us in [#1169](https://github.com/J-Tech-Japan/Sekiban/issues/1169). For new projects we recommend Sekiban DCB.
>
> Sekiban.Pure は現在メンテナンスモードです（機能追加なし・.NET 9 対応）。.NET 9 の EOL（2026-11-10）でライフサイクルを完了する予定ですが、本番利用中の方がいれば .NET 10/11 への対応を機能追加なしの互換移行として行います（Sekiban(Core) 0.25.0 で同方式を実施済み: 公開 API 無変更・保存データ双方向互換）。必要な方は [#1169](https://github.com/J-Tech-Japan/Sekiban/issues/1169) でお知らせください。新規プロジェクトには Sekiban DCB を推奨します。
<!-- SEKIBAN_PURE_POLICY_END -->


This package contains project templates for creating event sourcing applications with Sekiban.

## Installation

```bash
dotnet new install Sekiban.Pure.Templates
```

## Available Templates

### 1. Orleans Sekiban Aspire Template

Create a new event sourcing project using Orleans and .NET Aspire:

```bash
dotnet new sekiban-orleans-aspire -n YourProjectName
```

This template includes:
- Orleans-based distributed event sourcing
- .NET Aspire orchestration
- PostgreSQL and Azure Storage support
- Blazor web frontend
- Sample domain implementation

### 2. Dapr Sekiban Aspire Template

Create a new event sourcing project using Dapr and .NET Aspire:

```bash
dotnet new sekiban-dapr-aspire -n YourProjectName
```

This template includes:
- Dapr-based distributed event sourcing
- .NET Aspire orchestration
- PostgreSQL support (Cosmos DB optional)
- Dapr actors, state store, and pub/sub
- Blazor web frontend
- Sample domain implementation

## Orleans Template Configuration

### 1. App Host Configuration

Add PostgreSQL password to secrets.json:

```json
{
  "Parameters:postgres-password": "your_strong_password"
}
```

### 2. Optional: Cluster Settings

Modify AppHost Program.cs for your clustering needs:

```csharp
var storage = builder.AddAzureStorage("azurestorage")
    .RunAsEmulator(r => r.WithImage("azure-storage/azurite", "3.33.0"));
var clusteringTable = storage.AddTables("orleans-sekiban-clustering");
var grainStorage = storage.AddBlobs("orleans-sekiban-grain-state");

var postgresPassword = builder.AddParameter("postgres-password", true);
var postgres = builder
    .AddPostgres("orleansSekibanPostgres", password: postgresPassword)
    .WithPgAdmin()
    .AddDatabase("SekibanPostgres");

var orleans = builder.AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage);
```

## Dapr Template Configuration

### 1. Dapr Components

The Dapr template includes pre-configured components in the `dapr-components` directory:
- State store configuration
- Pub/sub configuration
- Dapr runtime configuration

### 2. Running with Dapr

The template automatically configures Dapr sidecars through Aspire. Simply run:

```bash
dotnet run --project YourProjectName.AppHost
```

## Common Features

Both templates include:
- Event sourcing with Sekiban.Pure
- CQRS pattern implementation
- Sample domain with User and WeatherForecast aggregates
- Unit test projects
- Aspire dashboard for monitoring
- PostgreSQL database support
- Blazor Server web frontend

## Learn More

- [Sekiban Documentation](https://github.com/J-Tech-Japan/Sekiban)
- [Orleans Documentation](https://docs.microsoft.com/en-us/dotnet/orleans)
- [Dapr Documentation](https://docs.dapr.io)
- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire)