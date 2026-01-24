# ストレージプロバイダー - Postgres / Cosmos DB / DynamoDB

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
> - [Orleans構成](10_orleans_setup.md)
> - [ストレージプロバイダー](11_storage_providers.md) (現在位置)
> - [テスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)

DCB は複数のストレージプロバイダーをサポートしています。

## 対応クラウドプラットフォーム

| プラットフォーム | イベントストア | スナップショット | Orleans クラスタリング | Orleans ストリーム |
|-----------------|---------------|-----------------|---------------------|-------------------|
| **Azure** | Cosmos DB / Postgres | Azure Blob Storage | Cosmos DB / Azure Table | Azure Queue |
| **AWS** | DynamoDB / Postgres | Amazon S3 | RDS PostgreSQL | Amazon SQS |

---

## Azure プラットフォーム

### Cosmos DB イベントストア

`Sekiban.Dcb.CosmosDb` (`src/Sekiban.Dcb.CosmosDb`). コンテナー構成:

- `events` (PartitionKey: `/id`)
- `tags` (PartitionKey: `/tag`)

登録は `AddSekibanDcbCosmosDbWithAspire()` を推奨。

```csharp
services.AddSekibanDcbCosmosDbWithAspire();
```

Cosmos の書き込みはベストエフォート トランザクションです。整合性は Executor の予約と Cosmos の設定に依存します。

### Azure Blob Storage スナップショット

マルチプロジェクションの状態が大きい場合は `Sekiban.Dcb.BlobStorage.AzureStorage` を使用して Blob Storage に退避。

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
    new AzureBlobStorageSnapshotAccessor(
        sp.GetRequiredKeyedService<BlobServiceClient>("MultiProjectionOffload"),
        "multiprojection-snapshots"));
```

### Azure 設定例

```json
{
  "Sekiban": {
    "Database": "cosmos"
  },
  "ORLEANS_CLUSTERING_TYPE": "cosmos",
  "ORLEANS_GRAIN_DEFAULT_TYPE": "blob"
}
```

---

## AWS プラットフォーム

### DynamoDB イベントストア

`Sekiban.Dcb.DynamoDB` (`src/Sekiban.Dcb.DynamoDB`). テーブル構成:

- `{prefix}_events` : イベント本体 (pk: パーティションキー, sk: ソートキー, GSI: gsi1pk/sortableUniqueId)
- `{prefix}_events-tags` : タグ検索用 (pk/sk, GSI: tagGroup/tagString)
- `{prefix}_events-projections` : プロジェクション状態 (pk/sk)

テーブルはアプリケーション起動時に自動作成されます (`DynamoDbContext.EnsureTablesAsync()`)。

DI 登録:

```csharp
// Aspire + LocalStack (開発)
services.AddSekibanDcbDynamoDb();

// AWS 本番
services.AddSekibanDcbDynamoDb(options =>
{
    options.EventsTableName = "sekiban-events-prod";
});
```

### Amazon S3 スナップショット

`Sekiban.Dcb.BlobStorage.S3` を使用して S3 にスナップショットを退避。

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
    new S3BlobStorageSnapshotAccessor(
        sp.GetRequiredKeyedService<IAmazonS3>("SnapshotBucket"),
        "sekiban-snapshots-prod"));
```

### AWS 設定例

```json
{
  "Sekiban": {
    "Database": "dynamodb"
  },
  "DynamoDb": {
    "EventsTableName": "sekiban-events-prod"
  },
  "AWS": {
    "Region": "ap-northeast-1"
  }
}
```

---

## Postgres イベントストア (共通)

`Sekiban.Dcb.Postgres` (`src/Sekiban.Dcb.Postgres`). Azure/AWS どちらでも利用可能。

テーブル構成:

- `dcb_events` : JSONB ペイロード + タグ + メタデータ
- `dcb_tags` : タグとイベントの紐づけ (タグ別検索用)

DI 登録:

```csharp
builder.Services.AddSekibanDcbPostgres(configuration);
// もしくは接続文字列を直接指定
builder.Services.AddSekibanDcbPostgres("Host=localhost;Database=sekiban_dcb;Username=postgres;Password=postgres");
```

マイグレーションは `Sekiban.Dcb.Postgres.MigrationHost` から実行するか、Aspire の初期化サービスに任せます。

---

## 設定のポイント

- `Sekiban:Database` に `postgres`、`cosmos`、または `dynamodb` を設定。
- Aspire を使う場合は各サービスを keyed サービスとして登録。
- 秘匿情報は KeyVault (Azure) や Secrets Manager (AWS) で管理。

## 運用メモ

- **Postgres**: `dcb_tags` のインデックス統計を監視し、VACUUM を定期実行。
- **Cosmos DB**: RU 消費量を監視し、自動スケールやパーティション設計を調整。
- **DynamoDB**: オンデマンドキャパシティ推奨。大規模なマルチプロジェクションは S3 にオフロード。
- **スナップショット**: Blob/S3 のライフサイクル管理で不要なものを削除。

## 今後の予定

DCB の Dapr 版は開発中です。現時点では Orleans ベースの実行環境をご利用ください。
