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

`Sekiban.Dcb.CosmosDb` メーター(`CosmosDbTelemetry.MeterName`)は、`sekiban.dcb.cosmos.tag_write.failures`(ラベル `reason`: `transient` | `corruption`)、`sekiban.dcb.cosmos.tag_write.retries`、`sekiban.dcb.cosmos.tag_write.retry_outcomes`(ラベル `outcome`: `recovered` | `exhausted`)、`sekiban.dcb.cosmos.event_write.partial_failures` を発行します。メトリクスのラベルは小さな固定集合から取られます。生のイベントIDやタグ文字列は非有界であるため、構造化ログと上記の例外にのみ含まれます。

#### タグインデックスの修復 (`CosmosDbTagRepairService`)

タグ行はイベントから完全に導出できるため、`tags` コンテナーは `events` コンテナーから再構築できます。`CosmosDbTagRepairService` はそのための運用者向けサーフェスです。`sortableUniqueId` の範囲でイベントをスキャンし、各 (イベント, タグ) ペアについて期待されるタグ行を導出し、欠けているものだけを作成します。

このサービスは **厳密に非破壊** です。存在しない行を作成することしかせず、行の削除・書き換え・正規化は決して行いません。また意図的に `IEventStore` の一部にはしていません — 運用者専用のジョブとして実行し、リクエストパスから公開しないでください。

```csharp
services.AddSekibanDcbCosmosDb(configuration);
services.AddSekibanDcbCosmosDbTagRepair();   // opt-in。AddSekibanDcbCosmosDb だけでは登録されない

// 運用者専用ジョブ内で:
var repair = await factory.CreateAsync(serviceId);   // 1インスタンス == 1つの (serviceId, events, tags) 系統

// 書き込む前に必ず確認する
var report = await repair.RepairAsync(new CosmosTagRepairOptions
{
    DryRun = true,                                    // デフォルト
    ToSortableUniqueIdInclusive = lastSettledId,      // 上限を固定。稼働中の書き込みは書き込みパス自身が処理する
    MaxEventsToScan = 10_000,
});

// その後、有界な run を再開しながら修復する
string? checkpoint = null;
do
{
    var run = await repair.RepairAsync(new CosmosTagRepairOptions
    {
        DryRun = false,
        ToSortableUniqueIdInclusive = lastSettledId,
        Checkpoint = checkpoint,
    }, cancellationToken);

    checkpoint = run.HasMore ? run.Checkpoint : null;
} while (checkpoint != null);
```

DI ではなくカスタムファクトリでストアを組み立てるホスト向けに、手動構築も可能です(`new CosmosDbTagRepairServiceFactory(context, containerResolver)`)。いずれの場合も、インスタンスは構築時に1つの `(serviceId, events コンテナー, tags コンテナー)` 系統に束縛されるため、系統をまたぐ修復は「非推奨」ではなく **構造的に不可能** です。

**レポートの分類**。すべての (イベント, タグ) キーは必ずいずれか1つに分類されます:

| 分類 | 意味 | 修復は書き込む? |
|---|---|---|
| `Present` | 導出された行が存在し、イベントと一致している。 | いいえ |
| `Missing` | このペアを索引する行が存在しない。 | **はい** — 書き込むのはこの分類のみ |
| `LegacyPresent` | 決定論的ID方式より前に書かれた行がこのペアを索引している。ランダムIDと壁時計の `createdAt` は想定内の差異であり、移行メタデータとして報告される。 | いいえ — レガシー行には一切触れない |
| `Duplicate` | 複数のレガシー行が同じペアを索引している(決定論的ID以前の再実行の残骸)。 | いいえ — 報告のみ。削減は破壊的操作でありスコープ外 |
| `Corrupt` | 行は存在するがイベントと矛盾している(`sortableUniqueId` / `eventType` / `tagGroup` のドリフト、または導出IDの位置にある行の内容不一致)。 | いいえ — 決して上書きしない |
| `Overflow` | `MaxRowsPerKey` の上限を超える数の行がこのペアを索引しており、分類できなかった。 | いいえ — 上限を上げて再確認する |

**運用ガイダンス**

- **最小権限**: ジョブに必要なのは `events` コンテナーの読み取りと、`tags` コンテナーの *作成* だけです。削除も置換も行わないため、`tags` コンテナーに対して `deleteItem` / `replaceItem` を持たない Cosmos ロールで十分です。コードを信頼するのではなく、プラットフォーム側で非破壊性を強制する方法として推奨します。
- **RUコスト**: イベントのスキャンはクロスパーティションクエリ(イベントはイベントごとにパーティション分割されるため)で、さらに (イベント, タグ) キーごとに `tags` コンテナーへのパーティション限定クエリが1回かかります。したがって RU はイベント数ではなく **キー数** に比例します(概ね `イベント数 × 1イベントあたりのタグ数`)。`MaxParallelism`(既定 4)が RU レートの調整ダイヤル、`MaxEventsToScan`(既定 10,000)が1回の run のコスト上限です。スロットリングは想定内で、サービスは Cosmos の `Retry-After` を短縮せずそのまま尊重します。
- **上限を固定する**: `ToSortableUniqueIdInclusive` には、確定済みと分かっているイベントを指定してください。スキャン中に書き込まれるイベントは書き込みパス自身が索引します。修復はクラッシュの残骸のためのものであり、稼働中のトラフィックのためのものではありません。
- **並行性**: run は稼働中の書き込みとも、別の run とも同時に安全に実行できます。通常の書き込みが「missing と分類したまさにその行」を先に書き込んだ場合、run はそれを「自分が書こうとしていた行」と認識して先へ進みます — 重複も、エラーも発生しません。
- **整合性の注意点**: 修復直後の読み取りは、アカウントに設定された Cosmos の整合性レベルによっては古い状態を観測する可能性があります。セッション整合性や結果整合性のもとでは、別セッションからのタグ検索が修復の書き込みに遅れることがあります。

#### 自動スイープ (`AddSekibanDcbCosmosDbTagSweep`) — opt-in

修復サービスは運用者が実行したときにしか動かないため、日常的に発生するクラッシュの残骸は誰かが気づくまで放置されます。スイープはこのギャップを埋めます。起動直後に直近のウィンドウに対して修復を実行し、任意で定期実行もします。

```csharp
services.AddSekibanDcbCosmosDb(configuration);
services.AddSekibanDcbCosmosDbTagSweep(sweep =>
{
    sweep.Enabled = true;                          // 既定はオフ(下記参照)
    sweep.Window = TimeSpan.FromHours(24);         // クラッシュの残骸は直近のもの。全期間の再構築は手動ジョブの仕事
    sweep.Interval = TimeSpan.FromHours(6);        // 省略すると起動時のみ
    sweep.MaxParallelism = 2;                      // 稼働トラフィックに譲る
    sweep.MaxEventsPerRun = 10_000;                // 1回の run の RU コスト上限
    sweep.RunBudget = TimeSpan.FromMinutes(5);     // 超過した run は次回に再開する
});
```

`RunBudget` に達した run は、それまでに処理し終えた分の進捗を保持します。チェックポイントは完了したイベントの先まで進み、次回はそこから始まります。したがって、1回でウィンドウを走査しきれないほど厳しいバジェットでも、同じ先頭を延々と再スキャンするのではなく、毎回確実に前進します。なお、ホストの停止はバジェット超過ではありません。スイープは単に停止し、何も保存しません。

**二重の opt-in**。`AddSekibanDcbCosmosDb` はスイープを登録せず、登録したとしても `Enabled` を設定するまで何も動きません。パッケージの参照やアップグレードだけでは、**ホステッドサービスも、ネットワークスキャンも、起動遅延も、必須の設定項目も一切増えません**。起動がブロックされることもありません(スイープはバックグラウンドで動き、`RunBudget` が1回の run を制約します)。スイープが失敗してもログに記録して次回リトライするだけで、**ホストを落とすことはありません**。

オプションとチェックポイントは **系統(lineage)ごと** です。`ServiceIds` でスイープ対象の系統を選択し(空ならホスト自身の service id)、各系統は独立したウィンドウと再開位置で処理されます。

**設定によって破壊的にすることはできません。** スイープからストレージへの唯一の経路は修復サービスであり、そのストアは削除・置換・upsert を表現できません。欠けている行を補充し、それ以外は分類するだけです。`LegacyPresent` や `Duplicate` の集合を何度スイープしても、作成される行は **ゼロ**、削除される行も **ゼロ** です。`Corrupt` と `Overflow` はテレメトリとログで通知するのみで、移行や削減を試みることはありません。これを変える設定も、コード経路も存在しません。

**定期実行を有効にする前に**: 手動の **dry run** を実行して RU コストを観測してください(上記のRUガイダンス参照)。その上で `MaxParallelism` は 2〜4 に保ち、負荷の低い時間帯に実行されるよう間隔を設計してください。

**レプリカ構成の場合**: すべてのレプリカはほぼ同時に起動するため、そのままでは一斉にスイープして RU を同時に跳ね上げます。`MaxStartupJitter`(既定30秒)が起動時の実行を分散させます。多数のレプリカで定期スイープを行う場合は、リーダー選出(またはリース取得)を行い1インスタンスからのみスイープすることを推奨します。ジッターは集中を薄めるだけで、作業の重複自体は防ぎません。なお重複実行は **安全** です(run は冪等で、run 同士とも稼働中の書き込みとも競合しません)。単に不要な RU を消費するだけです。

**スイープのテレメトリ**(メーター `Sekiban.Dcb.CosmosDb`): `sekiban.dcb.cosmos.tag_sweep.runs`(ラベル `outcome`: `completed` | `budget_exhausted` | `failed`)、`sekiban.dcb.cosmos.tag_sweep.repaired_rows`、`sekiban.dcb.cosmos.tag_sweep.corrupt_keys`、`sekiban.dcb.cosmos.tag_sweep.overflow_keys`。

#### スイープが保証 **しない** こと

依存する前に必ずお読みください。スイープは **最終的な修復であって、セーフティネットではありません**。

- **タグ読み取りをガードしません。** スイープを待つものは何もありません。`ReadEventsByTagAsync`、タグプロジェクター、`GetLatestTagAsync` は、呼ばれた瞬間に `tags` コンテナーにあるものをそのまま返します。
- **タグ欠落のウィンドウは残ります。** クラッシュはいつでも起こり得ますが、スイープが到達するのは次回の実行時です。クラッシュからその実行までの間、該当イベントは all-events リーダーからは可視、タグ検索からは不可視のままです。
- **その間、タグ整合アクターのベースラインは後退し得ます。** `GeneralTagConsistentActor` は楽観的並行性制御のベースラインを `tags` コンテナーから再構築するため、タグ行の欠落は修復が届くまでベースラインを静かに引き下げます。これは見落としではなく、**既知の制約** です。
- **修復直後の読み取りも古い可能性があります**(アカウントに設定された Cosmos の整合性レベルによる)。

これらのウィンドウを塞ぐ readiness ゲート、読み取り時の検証、コミットプロトコルは、意図的に **今回のスコープ外(将来課題)** です。このウィンドウを許容できないワークロード(とりわけ金銭に関わるもの)では、**Postgres プロバイダーを使用してください**。単一トランザクションでイベント/タグのアトミック性が保証され、ここで述べたギャップはいずれも存在しません。

#### レガシータグ行の移行(破壊的・運用者専用)

決定論的ID方式より前に書かれた行は、ランダムなドキュメントIDを持ちます。**それらは正常に機能します。** 修復サービスは意味的キーでそれらを認識し、タグ検索も見つけられるため、**正しさの観点で移行は必須ではありません**。このツールは整理のために存在し、(イベント, タグ) の行を1つの正規行に削減します。そしてそれは **ドキュメントの削除** によって行われます。Sekiban の中で削除を行うのは、これだけです。

`AddSekibanDcbCosmosDb` では登録されず、**自動スイープからは到達不能**で、決して自動実行されません。

**CLI**(`tools/SekibanDcbTagMigration`)は同一サービスへの薄いフロントエンドで、独自の破壊的ロジックを持ちません。持ちようがありません — タグ行の削除を表現するシームは `Sekiban.Dcb.CosmosDb` に対して `internal` であり、他のアセンブリからは削除を発行できないためです。

```bash
# 1. 計画。読み取り専用。どの行が削除されるかを正確に記した artifact を出力する。
sekiban-dcb-tag-migration plan \
  --connection "<cs>" --database SekibanDcb --service-id <id> \
  --plan tag-migration-plan.json

# 2. それを読む。この2段階フローの要点はここにある。

# 3. 適用。--confirm と --backup がなければ拒否される。
sekiban-dcb-tag-migration apply \
  --connection "<cs>" --database SekibanDcb --service-id <id> \
  --plan tag-migration-plan.json --backup removed-rows.json --confirm
```

サービスAPIも同じ2つの呼び出しです(`PlanAsync` → `ApplyAsync(plan, options)`。ファクトリの登録は `AddSekibanDcbCosmosDbLegacyTagMigration()`)。

**事故を防ぐもの**。以下はすべて、ドキュメントに触れる **前に** 拒否します。

| ゲート | 挙動 |
|---|---|
| 計画がない | `ApplyAsync` は計画しか受け取りません。**見せられていない行を削除することはできません**。 |
| `Confirm` がない | `CosmosTagMigrationNotAuthorizedException`。このフラグに寛容な既定値はありません。 |
| バックアップライタがない | `CosmosTagMigrationNotAuthorizedException`。Cosmos に undo はないため、エクスポートが復旧経路そのものです。 |
| 計画が生成後に改変された | フィンガープリントが内容と一致しない → `CosmosTagMigrationPlanRejectedException`。レビューされていない artifact は何も承認しません。 |
| 別系統の計画 | 拒否。インスタンスは構築時に1つの `(serviceId, events, tags)` に束縛されます。 |
| バックアップ書き込みが失敗 | 何も削除されません。バックアップを最初に書くのは、まさにこのためです。 |

**生存者ポリシー**。常に SEK-G2 の決定論的ID行(ドキュメントIDがイベントIDである行)が勝ちます。これは現在の書き込みパスが生成する行であり、将来のあらゆる書き込みが生成する行だからです。その行が存在しない場合、移行は**イベントから導出して作成**し(生存者の内容は書き込みパスが書いたであろう内容そのものになり、レガシーの癖が移行後に残りません)、**その後で**レガシー行を削除します。したがってキーが一瞬たりとも未索引になることはありません。レガシー行は昇格されず削除されるだけなので、判定を誤るタイブレークも存在しません。変化のない同じ状態を2回計画すれば、バイト単位で同一の artifact が得られます。

**並行性 — 「検証してから削除」ではなく、単一トランザクション**。(イベント, タグ) の全行は同一パーティションに存在するため、削減は **単一の Cosmos トランザクショナルバッチ** です。正規の生存者を条件付け(計画時に不在なら作成、存在したならその正確なバージョンでの replace-if-match)、各削除対象を delete-if-match し、**すべてを1つの原子境界**で実行します。

この形こそが要点です。読み取りで生存者を検証してから削除するのは「検査してから使用する」ことであり、その間に世界は動きます。検証の後に生存者が削除されても、削除は commit されてしまい、キーは**何にも索引されない**状態で残ります。再読み取りの頻度を上げても窓は狭まるだけで、閉じません。この設計に窓はありません。**キーは正規化されるか、何一つ変わらないかのどちらか**です。

- 計画後に生存者が出現/消失/書き換えられた → トランザクション拒否 → **1行も削除されない** → `StaleSurvivor`
- 計画後にいずれかの削除対象が動いた → トランザクション拒否 → **1行も削除されない(動いていない行すら)** → `LostRaceContentChanged`
- 監査は、commit されなかったトランザクションで行が削除されたと主張することは決してありません。

1トランザクションの上限(100操作 = 生存者1 + 削除対象99)を超えるキーは、**計画時に拒否**されます。複数トランザクションへの分割は、まさにこの設計が消し去った隙間を作り直すことになるためです。

**触らないもの**。イベントと矛盾する行は重複ではなく破損であり、その扱いを決めるのはこのツールの役割ではありません(`Skipped` として報告し、決して削除しません)。1キーあたりの上限を超える行数の場合も同様です(`Overflow`)。

**監査**。すべてのキーが監査エントリを生成します(生存者・削除した行・結果)。**触らなかったキーも含めて**記録されます。

**復旧**。バックアップファイルは、削除された行を `tags` コンテナーが保持しているのと同じ形の完全なドキュメントとして保持します。復元はそれらを再作成するだけです — 変換も再構築も不要です。

**引き続き計画中(未リリース)**: readiness ゲート。

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
