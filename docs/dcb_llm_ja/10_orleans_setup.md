# Orleans構成 - アクターベースで DCB を実行

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [コマンド・イベント・タグ・プロジェクター](03_aggregate_command_events.md)
> - [マルチプロジェクション](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [コマンドワークフロー](06_workflow.md)
> - [シリアライゼーションとドメイン型登録](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントUI (Blazor)](09_client_api_blazor.md)
> - [Orleans構成](10_orleans_setup.md) (現在位置)
> - [ストレージプロバイダー](11_storage_providers.md)
> - [テスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)

DCB のアクター実装は Orleans 上で動作します。テンプレートの AppHost は `UseOrleans` を通じてクラスタ設定を構築
します (`internalUsages/DcbOrleans.ApiService/Program.cs`)。

## 対応環境

| 環境 | クラスタリング | Grain ストレージ | ストリーム |
|-----|--------------|-----------------|----------|
| **開発 (Aspire)** | Localhost | Memory | Memory |
| **Azure** | Cosmos DB / Azure Table | Azure Blob | Azure Queue |
| **AWS** | RDS PostgreSQL (ADO.NET) | Memory / Custom | Amazon SQS |

---

## Azure 環境での設定

```csharp
builder.UseOrleans(config =>
{
    if (builder.Environment.IsDevelopment())
    {
        config.UseLocalhostClustering();
    }
    else
    {
        config.UseCosmosClustering(options => options.ConfigureCosmosClient(connectionString));
    }

    config.Configure<ClusterOptions>(opt =>
    {
        opt.ClusterId = "sekiban-dcb";
        opt.ServiceId = "sekiban-dcb-service";
    });
});
```

### Azure ストレージ

- Grain 永続化: Blob/Table/Cosmos から選択 (`ORLEANS_GRAIN_DEFAULT_TYPE`)
- TagState: Grain ストレージにスナップショットを保存可能
- マルチプロジェクション: `IBlobStorageSnapshotAccessor` で Blob Storage に退避

### Azure ストリーム

```csharp
config.AddAzureQueueStreams("EventStreamProvider", configurator =>
{
    configurator.ConfigureAzureQueue(options =>
    {
        options.QueueServiceClient = sp.GetKeyedService<QueueServiceClient>("DcbOrleansQueue");
        options.QueueNames = ["dcborleans-eventstreamprovider-0", "-1", "-2"];
    });
    configurator.ConfigureCacheSize(8192);
});
```

---

## AWS 環境での設定

AWS では Orleans クラスタリングに RDS PostgreSQL (ADO.NET)、ストリームに SQS を使用します。

```csharp
builder.UseOrleans(config =>
{
    if (builder.Environment.IsDevelopment())
    {
        config.UseLocalhostClustering();
        config.AddMemoryStreams("EventStreamProvider");
    }
    else
    {
        // RDS PostgreSQL でクラスタリング
        var rdsConnectionString = BuildRdsConnectionString();
        config.UseAdoNetClustering(options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = rdsConnectionString;
        });
        config.UseAdoNetReminderService(options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = rdsConnectionString;
        });

        // SQS ストリーム
        config.AddSqsStreams("EventStreamProvider", configurator =>
        {
            configurator.ConfigureSqs(options =>
            {
                options.Region = "ap-northeast-1";
                options.QueuePrefix = "orleans-stream-prod";
            });
        });
    }

    config.Configure<ClusterOptions>(opt =>
    {
        opt.ClusterId = "sekiban-dcb";
        opt.ServiceId = "sekiban-service";
    });
});
```

### Orleans スキーマの初期化 (AWS)

RDS PostgreSQL では Orleans のスキーマが必要です。アプリ起動時に自動でスキーマを作成する `OrleansSchemaInitializer` を使用します。

```csharp
var schemaInitializer = new OrleansSchemaInitializer(logger);
await schemaInitializer.InitializeAsync(rdsConnectionString);
```

---

## Grain 実装

- `TagConsistentGrain`: 予約処理 (GeneralTagConsistentActor をラップ)
- `TagStateGrain`: タグ状態キャッシュ (GeneralTagStateActor をラップ)
- `MultiProjectionGrain`: イベント処理とクエリ提供

実装は `src/Sekiban.Dcb.Orleans/Grains/*.cs` を参照。

## エグゼキューター登録

```csharp
builder.Services.AddSingleton<ISekibanExecutor, OrleansDcbExecutor>();
```

`OrleansActorObjectAccessor` が必要な Grain を見つけ、`GeneralSekibanExecutor` に渡します。

## ASP.NET 連携

`AddServiceDefaults()` が OpenTelemetry/HealthCheck/構成バインディングをまとめて設定します。
`app.MapHealthChecks("/health")` を追加して可用性を監視しましょう。

## デプロイ時の注意

### Azure
- `ORLEANS_CLUSTERING_TYPE` を `azuretable` / `cosmos` に設定
- Azure Queue を事前作成するか、プロビジョニング時に `IsResourceCreationEnabled` を有効にする

### AWS
- RDS PostgreSQL でスキーマが自動作成されます
- SQS キューは CDK で事前作成されます
- 環境変数で `Orleans__ClusterId` と `Orleans__ServiceId` を設定

### 共通
- サイロを水平スケールするとタグ Grain が自動で再配置されます

## Orleans なしでのテスト

`InMemorySekibanExecutor` を使えばサイロ無しでもコマンド処理を試せます。
