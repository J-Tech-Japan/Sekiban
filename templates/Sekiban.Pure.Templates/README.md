# Sekiban Event Sourcing Templates

<!-- SEKIBAN_PURE_POLICY_START -->
> **Sekiban.Pure support policy / サポート方針**
>
> Sekiban.Pure is in maintenance mode: no new features. Its main target is .NET 10, and it builds for .NET 10 and .NET 11 (Sekiban.Pure 9.x remains available for .NET 9). We apply security fixes only: fixes for vulnerabilities in Sekiban.Pure itself and dependency updates that resolve published security advisories. For new projects we recommend Sekiban DCB.
>
> Sekiban.Pure はメンテナンスモードです（機能追加なし）。メインの対応は .NET 10 で、.NET 10 と .NET 11 向けにビルドします（.NET 9 向けには Sekiban.Pure 9.x が引き続き利用できます）。対応するのはセキュリティ修正のみです（Sekiban.Pure 自身の脆弱性の修正と、公開済みのセキュリティ勧告を解消する依存関係の更新）。新規プロジェクトには Sekiban DCB を推奨します。
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