# ストレージプロバイダー - Postgres / Cosmos / Azure Storage

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
> - [ストレージプロバイダー](11_dapr_setup.md) (現在位置)
> - [テスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)

DCB のイベントストア実装は Postgres と Cosmos DB をサポートし、マルチプロジェクションのスナップショットには
Azure Blob Storage を利用できます。Dapr 連携は今後の対応予定です。

## Postgres イベントストア

`Sekiban.Dcb.Postgres` (`src/Sekiban.Dcb.Postgres`). テーブル構成:

- `dcb_events` : JSONB ペイロード + タグ + メタデータ
- `dcb_tags` : タグとイベントの紐づけ (タグ別検索用)

DI 登録:

```csharp
builder.Services.AddSekibanDcbPostgres(configuration);
// もしくは接続文字列を直接指定
builder.Services.AddSekibanDcbPostgres("Host=localhost;Database=sekiban_dcb;Username=postgres;Password=postgres");
```

マイグレーションは `Sekiban.Dcb.Postgres.MigrationHost` から実行するか、Aspire の初期化サービスに任せます。

## Cosmos DB イベントストア

`Sekiban.Dcb.CosmosDb` (`src/Sekiban.Dcb.CosmosDb`). コンテナー構成:

- `events` (PartitionKey: `/id`)
- `tags` (PartitionKey: `/tag`)

登録は `AddSekibanDcbCosmosDbWithAspire()` を推奨。

```csharp
services.AddSekibanDcbCosmosDbWithAspire();
```

Cosmos の書き込みはベストエフォート トランザクションです。整合性は Executor の予約と Cosmos の設定に依存します。

## スナップショットストレージ

マルチプロジェクションの状態が大きい場合は `Sekiban.Dcb.BlobStorage.AzureStorage` を使用して Blob Storage に退避。

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
    new AzureBlobStorageSnapshotAccessor(
        sp.GetRequiredKeyedService<BlobServiceClient>("MultiProjectionOffload"),
        "multiprojection-snapshots"));
```

## 設定のポイント

- `Sekiban:Database` に `postgres` または `cosmos` を設定。
- Aspire を使う場合は Table/Queue/Blob を keyed サービスとして登録。
- 秘匿情報は KeyVault やユーザーシークレットで管理。

## 運用メモ

- Postgres: `dcb_tags` のインデックス統計を監視し、VACUUM を定期実行。
- Cosmos: RU 消費量を監視し、自動スケールやパーティション設計を調整。
- Blob: スナップショットのライフサイクル管理 (不要なものは削除)。

## 今後の予定

DCB の Dapr 版は開発中です。現時点では Orleans ベースの実行環境をご利用ください。
