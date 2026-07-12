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
> - [コールドイベントとキャッチアップ](19_cold_events.md)
> - [マテリアライズドビュー基礎](20_materialized_view.md)

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

- `events` (PartitionKey: `/pk`)
- `tags` (PartitionKey: `/pk`)
- `multiProjectionStates` (PartitionKey: `/pk`)

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

## 整合性契約

このセクションでは `IEventStore.WriteSerializableEventsAsync` / `WriteEventsAsync` の、プロバイダーごとの実際のアトミック性保証を説明します。Cosmos の二段階書き込み設計自体は変更されていません。変わったのは、その書き込みが失敗したときの扱いです。

### Postgres — 現在の保証

`PostgresEventStore.WriteEventsAsync` はイベント行とタグ行を単一のデータベーストランザクション内で書き込みます。**イベントセットのアトミック性**(`WriteEventsAsync` 呼び出し内のすべてのイベントがコミットされるか、まったくコミットされないか)と、**イベント/タグのアトミック性**(タグ行を伴わずにイベントが可視化されることはない)の両方が保証されます。

### Cosmos DB — 現在の保証

`CosmosDbEventStore.WriteSerializableEventsAsync` は、**2つのフェーズにまたがるトランザクションを持たない**、2段階の書き込みを行います:

1. イベントドキュメントを並列に作成(`CreateItemAsync`)。イベントごとに1件、`{serviceId}|{eventId}` でパーティション分割。
2. その後、タグ行を per-tag-partition の `TransactionalBatch`(`{serviceId}|{tag}`)で書き込み。

これにより、現在次の2つの問題があります:

- **イベント/タグのクラッシュウィンドウ**: フェーズ1とフェーズ2の間、またはフェーズ2のタグバッチ処理中にクラッシュやホスト終了が発生すると、タグ行を伴わない永続化済みイベントが残ります。これらのイベントは `ReadAllEventsAsync` からは可視ですが、`ReadEventsByTagAsync`、タグプロジェクター、および(`GeneralTagConsistentActor` の楽観的並行性制御のベースラインにも使われる)`GetLatestTagAsync` からは不可視です。クラッシュはプロセス内の失敗ではないため、後述のどのポリシーでもこのウィンドウは防げません。閉じられるのは修復パスだけです。
- **複数イベントの部分的な可視化**: 複数イベントの `WriteEventsAsync` 呼び出しは、並列のイベント作成の途中で失敗することがあります。作成に成功したイベントは all-events リーダーに即座に可視化されますが、同じ呼び出し内の書き込みに失敗した兄弟イベントはどこにも記録されません。これは現在 `CosmosPartialEventWriteException` として報告され、可視化されたイベントIDと失敗したイベントIDの両方を通知します。また、これに対して**何も削除しません**。

タグ行は (イベント, タグ) のペアから **決定論的に導出** されます — `pk = {serviceId}|{tag}`、`id = {eventId}`、残りのフィールドは完全な `tags` 配列を保持するイベントドキュメントから導出されます(`Models/CosmosEvent.cs` と `Tags/CosmosTagIdentity.cs` を参照)。ここから2つの帰結が得られます。まず、タグ書き込みの再実行は安全です — 同一の行を再導出し、既に存在する行はそのまま受け入れ、部分書き込みで欠けた行だけを埋めます。そして `tags` コンテナーは **派生可能なインデックス** であり、常に `events` コンテナーから再構築できます。既存のタグ行の内容がイベントと矛盾する場合は `CosmosTagIndexCorruptionException` が送出され、その行が上書きされることは決してありません。このエラーは **リトライ不可** です — 何度試行しても同じ内容が導出されるためです。

#### 書き込み失敗ポリシー

`CosmosDbEventStoreOptions.WriteFailurePolicy` で、タグ書き込みフェーズがプロセス内で失敗したときの挙動を選択します:

| ポリシー | 挙動 |
|---|---|
| `Compatible`(**デフォルト**) | 従来リリースと同じ挙動: タグ書き込みをリトライせず、(現在は `[Obsolete]` の)`TryRollbackOnFailure` が設定されていれば — デフォルトは `true` です — 書き込み済みのイベントドキュメントをベストエフォートで **削除** します。 |
| `RollForward`(opt-in) | タグ書き込みを **リトライ** します(ジッター付き指数バックオフ、全体のデッドライン、429 時の Cosmos `Retry-After` の尊重、`CancellationToken` の監視)。部分書き込みで欠けた行に収束します。イベントは **決して削除されません**。リトライを使い切った場合は `CosmosTagWriteExhaustedException` がタグ行の欠落している可能性のあるイベントを通知し、それらのイベントは後の修復のために永続化されたまま残ります。 |

サーバーから送られた `Retry-After` はそのまま尊重されます。`MaxBackoff` が上限を課すのはクライアント自身のバックオフ曲線であって、サーバーの指示ではありません。したがって `MaxBackoff` より長い `Retry-After` が短縮されることは **ありません** — サーバーの準備が整う前に再試行しても、再び 429 を受けるだけだからです。ただし `MaxTotalDuration` は引き続き全体を制約します。指示に従うとデッドラインを超える場合は、早期に再試行するのではなく `CosmosTagWriteExhaustedException` で停止します。

**ロールバックは、all-events コンシューマー(とりわけマルチプロジェクション)が既に読み取っている可能性のある永続化済みイベントを削除します**。これはそれらの状態を不可逆に汚染します。さらにプロセス内の例外時にしか動作しないため、クラッシュ後には決して実行されません。新規デプロイでは `RollForward` を推奨します:

```csharp
services.AddSekibanDcbCosmosDb(
    configuration,
    options => options.WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward);
```

デフォルトは現行リリースラインでは `Compatible` のままです。これは、**パッケージのアップグレードだけでは既存デプロイの挙動が一切変わらない** ことを保証するためです。デフォルトが `RollForward` に切り替わるのは、移行手順を文書化したメジャーバージョン境界のみです。

#### テレメトリ

`Sekiban.Dcb.CosmosDb` メーター(`CosmosDbTelemetry.MeterName`)は、`tag_write.failures`(ラベル `reason`: `transient` | `corruption`)、`tag_write.retries`、`tag_write.retry_outcomes`(ラベル `outcome`: `recovered` | `exhausted`)、`event_write.partial_failures` を発行します。メトリクスのラベルは小さな固定集合から取られます。生のイベントIDやタグ文字列は非有界であるため、構造化ログと上記の例外にのみ含まれます。

**引き続き計画中(未リリース)**: タグインデックス修復API、opt-in のスタートアップスイープ。パッケージをアップグレードするだけでは修復/スイープの動作は有効になりません。今後、明示的な opt-in として提供されます。なお、将来的に修復がリリースされた後も、修復直後の読み取りは、設定された Cosmos の整合性レベルによっては古い状態を観測する可能性がある点に注意してください。

### プロバイダーの選び方

アトミックなイベント/タグの可視性やイベントセットのアトミック性を必要とするワークロード(例: 金銭に関わるワークフロー)には **Postgres** を推奨します。Sekiban エコシステムにおける具体例:

- [SekibanWasmRuntime](https://github.com/J-Tech-Japan/SekibanWasmRuntime) はイベントストアのデフォルトが Postgres で、Cosmos は opt-in です。
- SekibanAsAService は管理系(management)とランタイム系(runtime)で別系統のコンテナーを使用しており、それぞれ独立してプロバイダーを選択できます。コンテナーごとに上記と同じ「アトミック性が必要なら Postgres」というガイドラインを適用してください。

## マテリアライズドビュー用ストレージ

マテリアライズドビューは現在 PostgreSQL 向けに実装されています。

- `Sekiban.Dcb.MaterializedView` : 共通契約と catch-up worker
- `Sekiban.Dcb.MaterializedView.Postgres` : registry、executor、行アクセス、table update
- `Sekiban.Dcb.MaterializedView.Orleans` : Orleans grain 制御と query accessor

これはメインの event store package とは別レイヤーです。現在の PoC では、イベントストアの DB と
マテリアライズドビューの DB を分離した構成も取れます。詳細は [マテリアライズドビュー基礎](20_materialized_view.md)
を参照してください。

## 関連資料

現在のインターナルユースで使っているコールドイベントの書き出し、ハイブリッドリード、キャッチアップワーカー構成については [コールドイベントとキャッチアップ](19_cold_events.md) を参照してください。
