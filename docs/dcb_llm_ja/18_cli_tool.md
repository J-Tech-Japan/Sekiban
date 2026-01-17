# CLIツール - Sekiban DCB

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [コマンド、イベント、タグ、プロジェクター](03_aggregate_command_events.md)
> - [マルチプロジェクション](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [コマンドワークフロー](06_workflow.md)
> - [シリアライゼーションとドメイン型](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントUI（Blazor）](09_client_api_blazor.md)
> - [Orleansセットアップ](10_orleans_setup.md)
> - [ストレージプロバイダー](11_dapr_setup.md)
> - [テスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [値オブジェクト](15_value_object.md)
> - [デプロイメントガイド](16_deployment.md)
> - [カスタムマルチプロジェクターシリアライゼーション](17_custom_multiprojector_serialization.md)
> - [CLIツール](18_cli_tool.md) (現在のページ)

## 概要

Sekiban DCBは、プロジェクション状態の管理、イベントの検査、ローカルSQLiteキャッシュの操作のためのコマンドラインインターフェース（CLI）ツールを提供します。CLIは以下の用途に特に有用です：

- マルチプロジェクション状態のビルドと再ビルド
- イベントとプロジェクション状態の検査によるデバッグ
- オフライン開発のためのローカルSQLiteキャッシュの管理
- プロファイルを使用した複数環境の切り替え

## プロジェクトセットアップ

### CLIプロジェクトの作成

新しいコンソールアプリケーションを作成し、必要な参照を追加します：

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFrameworks>net10.0;net9.0</TargetFrameworks>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <UserSecretsId>your-cli-secrets-id</UserSecretsId>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.Extensions.Configuration.UserSecrets" Version="9.0.1" />
        <PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.1" />
        <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
    </ItemGroup>

    <ItemGroup>
        <!-- ドメインプロジェクト -->
        <ProjectReference Include="..\YourApp.Domain\YourApp.Domain.csproj"/>

        <!-- Sekiban DCBパッケージ -->
        <ProjectReference Include="..\..\src\Sekiban.Dcb.WithResult\Sekiban.Dcb.WithResult.csproj"/>
        <ProjectReference Include="..\..\src\Sekiban.Dcb.Postgres\Sekiban.Dcb.Postgres.csproj"/>
        <ProjectReference Include="..\..\src\Sekiban.Dcb.CosmosDb\Sekiban.Dcb.CosmosDb.csproj"/>
        <ProjectReference Include="..\..\src\Sekiban.Dcb.Sqlite\Sekiban.Dcb.Sqlite.csproj"/>
    </ItemGroup>
</Project>
```

### 基本的なプログラム構造

```csharp
using System.CommandLine;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Sqlite.Services;
using Sekiban.Dcb.Storage;
using YourApp.Domain;

// 設定をビルド
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
    .Build();

// ルートコマンドを作成
var rootCommand = new RootCommand("CLIツール - プロジェクションとイベントの管理");

// コマンドを追加...
rootCommand.AddCommand(buildCommand);
rootCommand.AddCommand(statusCommand);
// など

return await rootCommand.InvokeAsync(args);
```

## マルチプロファイル設定

CLIは複数のプロファイルをサポートし、異なる環境（開発、ステージング、本番）への接続を切り替えることができます。

### User Secretsでプロファイルを設定

```bash
# User Secretsを初期化
dotnet user-secrets init

# "dev"プロファイルをセットアップ
dotnet user-secrets set "Profiles:dev:Database" "postgres"
dotnet user-secrets set "Profiles:dev:ConnectionString" "Host=localhost;Database=sekiban;..."

# Cosmos DBを使用する"stg"プロファイルをセットアップ
dotnet user-secrets set "Profiles:stg:Database" "cosmos"
dotnet user-secrets set "Profiles:stg:CosmosConnectionString" "AccountEndpoint=https://...;AccountKey=...;"
dotnet user-secrets set "Profiles:stg:CosmosDatabase" "SekibanDcb"

# デフォルトプロファイルを設定
dotnet user-secrets set "DefaultProfile" "dev"
```

### プロファイル設定構造

```json
{
  "Profiles": {
    "dev": {
      "Database": "postgres",
      "ConnectionString": "Host=localhost;Database=sekiban;Username=..."
    },
    "stg": {
      "Database": "cosmos",
      "CosmosConnectionString": "AccountEndpoint=https://...;AccountKey=...;",
      "CosmosDatabase": "SekibanDcbStaging"
    },
    "prod": {
      "Database": "cosmos",
      "CosmosConnectionString": "AccountEndpoint=https://...;AccountKey=...;",
      "CosmosDatabase": "SekibanDcbProd"
    }
  },
  "DefaultProfile": "dev"
}
```

### プロファイルエイリアスキー

以下のエイリアスが便利に使用できます：

| エイリアスキー | マップ先 |
|---------------|---------|
| `Database` | `Sekiban:Database` |
| `ConnectionString` | `ConnectionStrings:DcbPostgres` |
| `PostgresConnectionString` | `ConnectionStrings:DcbPostgres` |
| `CosmosConnectionString` | `ConnectionStrings:SekibanDcbCosmos` |
| `CosmosDatabase` | `CosmosDb:DatabaseName` |

### プロファイル解決の実装

```csharp
static IConfiguration? ResolveProfile(IConfiguration configuration, string? profileName)
{
    var profilesSection = configuration.GetSection("Profiles");
    var profileEntries = profilesSection.GetChildren().ToList();
    var hasProfiles = profileEntries.Count > 0;

    var resolvedProfile = profileName;
    if (string.IsNullOrWhiteSpace(resolvedProfile))
    {
        var defaultProfile = configuration["DefaultProfile"];
        if (!string.IsNullOrWhiteSpace(defaultProfile))
        {
            resolvedProfile = defaultProfile;
        }
        else if (hasProfiles)
        {
            Console.WriteLine("Error: プロファイルが指定されていません。--profileオプションを使用してください。");
            Console.WriteLine($"利用可能なプロファイル: {string.Join(", ", profileEntries.Select(p => p.Key))}");
            return null;
        }
        else
        {
            return configuration;
        }
    }

    if (!hasProfiles)
    {
        Console.WriteLine($"Error: プロファイル '{resolvedProfile}' が定義されていません。");
        return null;
    }

    var profileSection = profilesSection.GetSection(resolvedProfile!);
    if (!profileSection.Exists())
    {
        Console.WriteLine($"Error: プロファイル '{resolvedProfile}' が見つかりません。");
        return null;
    }

    // プロファイルオーバーライドで設定をビルド
    var prefix = $"Profiles:{resolvedProfile}:";
    var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    foreach (var pair in profileSection.AsEnumerable())
    {
        if (pair.Value == null) continue;
        var key = pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? pair.Key[prefix.Length..]
            : pair.Key;
        overrides[key] = pair.Value;
    }

    // エイリアスをマップ
    void MapAlias(string aliasKey, string targetKey)
    {
        if (overrides.TryGetValue(aliasKey, out var value) && !overrides.ContainsKey(targetKey))
            overrides[targetKey] = value;
    }

    MapAlias("Database", "Sekiban:Database");
    MapAlias("CosmosConnectionString", "ConnectionStrings:SekibanDcbCosmos");
    MapAlias("CosmosDatabase", "CosmosDb:DatabaseName");
    MapAlias("ConnectionString", "ConnectionStrings:DcbPostgres");

    return new ConfigurationBuilder()
        .AddConfiguration(configuration)
        .AddInMemoryCollection(overrides!)
        .Build();
}
```

## 利用可能なコマンド

### profiles

利用可能なすべてのプロファイルを一覧表示：

```bash
dotnet run -- profiles
```

出力：
```
=== Available Profiles ===

  - dev (default)
  - stg
  - prod
```

### status

すべてのプロジェクション状態のステータスを表示：

```bash
dotnet run -- status --profile dev
```

オプション：
- `--profile, -P` - プロファイル名
- `--database, -d` - データベースタイプ（postgres/cosmos）
- `--connection-string, -c` - PostgreSQL接続文字列
- `--cosmos-connection-string` - Cosmos DB接続文字列
- `--cosmos-database` - Cosmos DBデータベース名

### build

マルチプロジェクション状態をビルドまたは再ビルド：

```bash
dotnet run -- build --profile dev --force --verbose
```

オプション：
- `--profile, -P` - プロファイル名
- `--min-events, -m` - ビルド前の最小イベント数（デフォルト: 3000）
- `--projector, -p` - ビルドする特定のプロジェクター
- `--force, -f` - 状態が存在しても強制的に再ビルド
- `--verbose, -v` - 詳細出力を表示

### save

プロジェクション状態のJSONをファイルにエクスポート：

```bash
dotnet run -- save --profile dev --projector MyProjector --output-dir ./output
```

### delete

プロジェクション状態を削除：

```bash
dotnet run -- delete --profile dev --projector MyProjector
```

### tag-events

特定のタグのすべてのイベントを取得してエクスポート：

```bash
dotnet run -- tag-events --profile dev --tag "User:12345" --output-dir ./output
```

### tag-state

特定のタグの現在の状態をプロジェクトして表示：

```bash
dotnet run -- tag-state --profile dev --tag "User:12345" --projector UserProjector
```

### tag-list

イベントストア内のすべてのタグを一覧表示：

```bash
dotnet run -- tag-list --profile dev --tag-group User --output-dir ./output
```

### projection

プロジェクションの現在の状態を表示：

```bash
dotnet run -- projection --profile dev --projector MyProjector
```

## ローカルSQLiteキャッシュ

CLIは、より高速な開発のためにリモートイベントをローカルSQLiteデータベースにキャッシュする機能をサポートしています。

### Sekiban.Dcb.Sqliteパッケージ

`Sekiban.Dcb.Sqlite`パッケージは以下を提供します：

- `SqliteEventStore` - 完全な`IEventStore`実装
- `SqliteMultiProjectionStateStore` - 完全な`IMultiProjectionStateStore`実装
- `EventStoreCacheSync` - リモートからローカルへの同期ヘルパー
- タグ操作のためのCLIサービス

### cache-sync

リモートイベントをローカルSQLiteキャッシュに同期：

```bash
dotnet run -- cache-sync --profile dev --cache-dir ./cache --safe-window 10
```

オプション：
- `--cache-dir, -C` - キャッシュディレクトリ（デフォルト: ./cache）
- `--safe-window` - キャッシュから除外するイベントの分数（デフォルト: 10）

セーフウィンドウは、まだ進行中またはコミットされていないイベントのキャッシュを防止します。

### cache-stats

ローカルキャッシュの統計情報を表示：

```bash
dotnet run -- cache-stats --cache-dir ./cache
```

出力：
```
=== Cache Statistics ===

Cache Directory: ./cache

Cache File: ./cache/events.db
File Size: 15.2 MB
Last Modified: 2025-01-17 09:30:45 UTC

Total Events: 42,000

Cache Metadata:
  Remote Endpoint: https://myaccount.documents.azure.com
  Database Name: SekibanDcb
  Last Sync: 2025-01-17 09:30:45 UTC
  Schema Version: 1.0

Tags: 150 total across 5 groups
  User: 100 tags
  Order: 30 tags
  Product: 20 tags
```

### cache-clear

ローカルキャッシュをクリア：

```bash
dotnet run -- cache-clear --cache-dir ./cache
```

## サービスのビルド

```csharp
static IServiceProvider BuildServices(string connectionString, string databaseType, string cosmosDatabaseName)
{
    var services = new ServiceCollection();
    var domainTypes = YourDomainType.GetDomainTypes();

    services.AddSingleton(domainTypes);

    if (databaseType.ToLowerInvariant() == "cosmos")
    {
        services.AddSingleton<IEventStore>(sp =>
        {
            var client = new CosmosClient(connectionString);
            return new CosmosEventStore(client, cosmosDatabaseName, domainTypes);
        });
        services.AddSingleton<IMultiProjectionStateStore>(sp =>
        {
            var client = new CosmosClient(connectionString);
            return new CosmosMultiProjectionStateStore(client, cosmosDatabaseName, domainTypes);
        });
    }
    else
    {
        services.AddSekibanDcbPostgres(connectionString);
    }

    // CLIサービスを追加
    services.AddSekibanDcbCliServices();

    return services.BuildServiceProvider();
}
```

## 使用例

```bash
# プロファイル一覧
dotnet run --framework net9.0 -- profiles

# デフォルトプロファイルでステータス確認
dotnet run --framework net9.0 -- status

# 特定のプロファイルでステータス確認
dotnet run --framework net9.0 -- status --profile prod

# 強制再ビルドでプロジェクションをビルド
dotnet run --framework net9.0 -- build --profile dev -f -v

# ローカルキャッシュに同期
dotnet run --framework net9.0 -- cache-sync --profile prod

# ローカルキャッシュでオフライン作業
dotnet run --framework net9.0 -- tag-list -d sqlite -c "./cache/events.db"
```

## リファレンス実装

完全なリファレンス実装については、以下を参照してください：
- `dcb/internalUsages/DcbOrleans.Cli/Program.cs`

この実装には、すべてのコマンド、プロファイルサポート、キャッシュ管理機能が含まれています。
