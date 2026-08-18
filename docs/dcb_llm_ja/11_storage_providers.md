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

## 受動的なプロジェクション状態レジストリ (SEK-G24 / dcb-v10.10.0)

各プロバイダーは、プロジェクション状態ストアと同時に受動的な `IProjectionStatusStore` と reader を登録します。
`IProjectionStatusReader` は Grain を解決せず、heartbeat とイベントストアの件数だけで状況を組み立てます。
分母は service ごとに sampling window（既定 5 秒）あたり 1 回、残数は bounded parallelism で distinct な
traversed cursor ごとに取得し、`SampledAtUtc` を付けた best-effort サンプルとして返します。CAS の行 identity は
`(ServiceId, ProjectorName, ProjectorVersion, ClusterId)` で、`ActivationId` は行データとして保持します。1 つの
`(ProjectorName, ProjectorVersion)` に fresh な cluster 行が複数ある場合は conflict として返し、古い activation
の replacement が別行へ書き込まれることも、last-write-wins で隠されることもありません。

保存形式は次のとおりです。

- PostgreSQL と SQLite は `dcb_projection_statuses` 専用テーブルを、既存 DB に対しても自動作成します。heartbeat
  は expected-sequence CAS で書き込みます。
- Cosmos DB と DynamoDB は projection snapshot と同じ領域に保存し、`documentType = "projectionStatus"` を付けます。
  snapshot の list/delete/scan/latest-version は status 行を除外し、discriminator のない旧 snapshot は読み込めます。
- DynamoDB の status 一覧はこの slice では bounded な filtered scan を使います。フリート規模で必要になった場合だけ
  status 専用 GSI を追加する、という escalation path です。
- 状態の読み取りと件数サンプルは projection state への書き込みではなく、Grain の activation も必要ありません。

serialized 境界は新しい `ISerializedProjectionStatusReader` の V1 envelope です。`ServiceId` は常にサーバー側
provider から決まり、ホストの endpoint は既定で deny としてください。必要な場合のみ operator 用 policy を明示し、
例えば `RequireAuthorization("ProjectionStatusOperator")` を指定します。`AllowAnonymous` は使わず、既存の
`ISerializedSekibanDcbExecutor` は変更しません。これは dcb-v10.10.0 の SEK-G24 release note です。

### projection-status heartbeat の回復 (SEK-G35 / dcb-v10.16.0)

projection-status heartbeat は activation 時点で writer identity 全体（service、projector 名、projector version、cluster）を
固定します。rolling deployment 中に host を再作成しても、その activation は新しく登録された host version へ行を暗黙に移さず、
固定された version に書き続けます。新しい activation は自然に新しい version を使用します。

expected-sequence CAS はすべての provider で fail-closed のままです。特に PostgreSQL と SQLite は初回 insert 後に update
経路が到達不能になることがなくなり、physical row が存在するときだけ update を試みます。expected sequence が非ゼロなのに
行がない場合は拒否されます。次の scheduled heartbeat が local fence を rebase した後、通常の sequence-zero create を
試みることはありますが、失敗した同じ operation で無条件 insert を行うことはありません。Cosmos DB も固定された document
identity と provider precondition により同じ契約を守ります。

既存の serialized V1 envelope は凍結されたままです。rolling deployment の診断が必要な operator は、追加された V2 reader
envelope を使用できます。V2 は expected/observed projector version と、行が current、version mismatch、または
stale/orphan のどれかを返します。これらは observation のための情報であり、provider が旧 version や他 cluster の行を
自動削除することはありません。rollout や incident analysis に不要になったことを確認してから、明示的な retention policy を
適用してください。

### 行タイムスタンプと独立した version match (SEK-G36 / dcb-v10.17.0)

`ProjectionStatusSnapshot.RecordedAtUtc` は heartbeat 行とともに commit された正確なタイムスタンプです。reader の
best-effort な観測時刻である `SampledAtUtc` から導出されるものではなく、行どうしの tie breaker にも使いません。in-process
snapshot は `RecordedAtUtc` と独立した `ProjectionStatusVersionMatch` (`Unknown = 0`、`Match = 1`、`Mismatch = 2`) を公開します。
V1 の byte は凍結されたままです。新しい事実は加法的な V2 wrapper から公開され、入れ子の `Snapshot` は V1 形状のままです。

consumer は次の 5 ステップに従ってください。

1. caller が比較すべき expected version を持つときだけ `ExpectedProjectorVersion` を指定します。V2 の
   `ProjectorVersion` と `ExpectedProjectorVersion` の request precedence は未決定なので、両方の request property を同時に
   設定しないでください。
2. freshness とは独立に `VersionMatch` を確認します。expected が null、空文字、または whitespace なら `Unknown` です。
   equality は ordinal かつ case-sensitive で、それ以外は `Mismatch` です。
3. `IsFresh` は別の軸として確認します。commit された `RecordedAtUtc` が freshness window 内にあり、かつ optional lease が
   expire していない場合にだけ true になります。
4. caller 自身の authority rule を適用するときも、すべての observation と既存の conflict signal を保持します。fresh mismatch
   は依然として fresh な observation であり、fresh matched row が 2 行なら依然として conflict です。
5. escalation、selection、retention、cleanup は明示的な consumer policy でのみ行います。stale mismatch は orphan
   *candidate* にすぎず、削除の authorization ではありません。dcb は行を fold、filter、`IsOrphan` 判定、削除しません。

次の 3 つの近道は authority rule ではありません。ordinal の最大 `ProjectorVersion` を選ばないでください
(`1.0.9` は `1.0.10` より大きく並びます)。最大 `Sequence` も選ばないでください（これは行ごとの CAS fence であり、orphan の
値の方が大きいことがあります）。最大の `LastAppliedSortableUniqueId` も選ばないでください（新しい current version はまだ
catch-up 中であることがあります）。2 つの独立した軸と caller の明示的な policy を使ってください。

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

**サービスAPI** は `Sekiban.Dcb.CosmosDb` パッケージに含まれます。`AddSekibanDcbCosmosDbLegacyTagMigration()` でファクトリを登録し、`PlanAsync` → `ApplyAsync(plan, options)` を呼び出します。

**サービスAPI を使用してください。** これがサポートされる経路であり、すでに参照しているパッケージに含まれています。CLI が呼び出しているのも、結局これと同じものです。

**CLI は配布されておらず、それを含むリリースタグも現時点では存在しません。** `tools/SekibanDcbTagMigration` はパッケージ化も公開もされておらず、`dotnet tool` でもありません。インストール可能な実行ファイルを生成するリリースは存在しません。さらに、この CLI は**最新のリリースタグより後に**追加されたため、公開済みの `dcb-v*` タグをチェックアウトしても、そのツリーにツールは存在せず、`dotnet run --project tools/SekibanDcbTagMigration` は動作しません。CLI は同一サービスへの薄いフロントエンドで、独自の破壊的ロジックを持ちません(持ちようがありません — タグ行の削除を表現するシームは `Sekiban.Dcb.CosmosDb` に対して `internal` であり、他のアセンブリからは削除を発行できないためです)。

それでも CLI を実行したい場合は、**自分で明示的にレビューしたソースリビジョンから実行**してください。そして**チェックアウトする前に**、そのリビジョンに実際にツールが含まれているかを確認してください。

```bash
git clone https://github.com/J-Tech-Japan/Sekiban.git
cd Sekiban

# この ref にツールは存在するか?(使用したい ref に置き換えてください)
REF=main
git cat-file -e "$REF:tools/SekibanDcbTagMigration/SekibanDcbTagMigration.csproj" \
  && echo "tool present at $REF" \
  || echo "tool ABSENT at $REF — この ref は使わないこと"

git checkout "$REF"
```

将来 CLI を含むリリースタグが出たら、ブランチよりそのタグを優先し、信頼する前に同じ存在チェックで確認してください。**csproj の `<PackageVersion>` を grep してリリースを検証してはいけません** — タグ付きソース内のその値はビルド時のプレースホルダであり、公開されたバージョンではありません。

これだけ慎重になる理由は単純です。行を書き込んだコードとは別のリビジョンでビルドしたツールは、手元に存在しない世界を記述した計画を平然と出力します。そしてその計画は**削除を承認するもの**です。

レビュー済みのチェックアウトができたら、2段階フローは次のとおりです。

```bash
# 計画。読み取り専用。どの行が削除されるかを正確に記した artifact を出力する。
dotnet run --project tools/SekibanDcbTagMigration -- plan \
  --connection "<cs>" --database SekibanDcb --service-id <id> \
  --plan tag-migration-plan.json

# それを読む。この2段階フローの要点はここにある。

# 適用。--confirm と --backup がなければ拒否される。
dotnet run --project tools/SekibanDcbTagMigration -- apply \
  --connection "<cs>" --database SekibanDcb --service-id <id> \
  --plan tag-migration-plan.json --backup removed-rows.json --confirm
```

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

**触らないもの**。イベントと矛盾する行は重複ではなく破損であり、その扱いを決めるのはこのツールの役割ではありません(`Skipped` として報告し、決して削除しません)。これは **正規行そのもの** にも当てはまります。決定論的IDの位置にある行がイベントと矛盾する場合、そのキーは丸ごと放置され、アクションは一切計画されません。計画してしまうと有害です — 実行時は replace で生存者を条件付けるため、破損した行をイベントの内容で静かに上書きして「直して」しまい、その上でキーの唯一の別記録であったレガシー行を削除することになるからです。1キーあたりの上限を超える行数の場合も同様に放置されます(`Overflow`)。

**監査**。すべてのキーが監査エントリを生成します(生存者・削除した行・結果)。**触らなかったキーも含めて**記録されます。

**復旧**。バックアップファイルは、削除された行を `tags` コンテナーが保持しているのと同じ形の完全なドキュメントとして保持します。復元はそれらを再作成するだけです — 変換も再構築も不要です。

**引き続き計画中(未リリース)**: readiness ゲート。

### プロバイダーの選び方

アトミックなイベント/タグの可視性やイベントセットのアトミック性を必要とするワークロード(例: 金銭に関わるワークフロー)には **Postgres** を推奨します。Sekiban エコシステムにおける具体例:

- [SekibanWasmRuntime](https://github.com/J-Tech-Japan/SekibanWasmRuntime) はイベントストアのデフォルトが Postgres で、Cosmos は opt-in です。
- SekibanAsAService は管理系(management)とランタイム系(runtime)で別系統のコンテナーを使用しており、それぞれ独立してプロバイダーを選択できます。コンテナーごとに上記と同じ「アトミック性が必要なら Postgres」というガイドラインを適用してください。

## 耐久性ディスクリプタと本番ガード

**名前は根拠になりません。** `InMemoryDcbExecutor` はテスト用の型に見えますが、ある本番システムはこれを `ISekibanExecutor` として登録していました。一引数コンストラクターが黙ってプライベートなインメモリイベントストアを作り、コマンドはすべて成功し、設定済みの Cosmos アカウントには**イベントが1件も届きませんでした**。起動時に検知できなかったのは、**問い合わせる手段がなかった**からです。

これからは問い合わせられます。

### 実行時に問える 2 つの軸

すべての組み込みストア/エグゼキューターが、**型名でも属性でも呼び出した `Add...` メソッドでもなく、生きたインスタンスから**次を申告します。

| 軸 | 値 | 申告する側 |
|---|---|---|
| ストレージ耐久性 | `Durable` / `Volatile` / `Unknown` + プロバイダー名 | イベントストア **および** プロジェクション状態ストア(とそのサービス別ファクトリー) |
| エグゼキューターランタイム | `DistributedRuntime` / `TestingInProcess` / `Unknown` + ランタイム名 | エグゼキューター(実際に載っているアクターアクセサーに問い合わせて回答) |

組み込みの自己申告: Postgres / Cosmos DB / DynamoDB → `Durable`、InMemory → `Volatile`、Orleans → `DistributedRuntime`、`InMemoryDcbExecutor` → `TestingInProcess`。

**実行時解決である理由**を示す 2 点:

- **Sqlite はファイルなら `Durable`、`:memory:` なら `Volatile`** — 同じクラス、同じ名前、正反対の保証です。どちらを掴んだかを知っているのは生きたインスタンスだけです。
- **デコレーターはデータが実際に着地する先を申告します。** volatile なストアを包んだ `HybridEventStore` は *volatile* です。包んだだけで耐久性を得ることはできません。

`Unknown` は中立の値ではありません。「申告を拒んだ」という意味であり、ガードはこれを危険側として扱います。**沈黙は耐久性の約束ではないからです。**

### ガード

```csharp
// opt-in。ライブラリが勝手に登録することはありません。
// 「ホストを起動させない」判断を勝手に下すライブラリは、いつか必ずその判断を間違えます。
builder.Services.AddSekibanDcbProductionGuard();
```

チェック対象の登録より**後**に呼び出してください。起動時(すべての登録が済み、ホストが何かを処理する前)に、コンテナーが**実際に構築したもの**を解決し、各インスタンスに問い合わせ、バナーを出力します。そして Production 環境では、次のいずれかであればホストの起動を拒否します。

- エグゼキューターが `DistributedRuntime` でない(= `TestingInProcess` または `Unknown`)
- いずれかのストアが `Durable` でない(= `Volatile` または `Unknown`)

Production 以外では検証を行わず、ログ出力のみです。**Development の挙動は変わりません。**

### 唯一のオーバーライドと、存在しないオーバーライド

```csharp
builder.Services.AddSekibanDcbProductionGuard(options =>
{
    options.AllowVolatileStorageInProduction = true;   // ストレージのみ
    options.ProductionEnvironmentNames.Add("prod-eu"); // 本番環境名が "Production" でない場合
});
```

`AllowVolatileStorageInProduction` は 2 つの意味で意図的に狭く作られています。

- **ストレージのみ。** テスト用エグゼキューターを許可することはできません。これを設定したうえで Production に `InMemoryDcbExecutor` を登録しても、**ホストは起動を拒否します**。
- **`Volatile` のみ。`Unknown` は決して許可しません。** このオプションを設定するとは「volatile だと**申告した**ストアを見たうえで、それでよいと判断した」という意味です。何も申告しないストアは、判断の対象すら与えていません。したがってこのオーバーライドを有効にしても `Unknown` は fail-closed のままです。`Unknown` を解消する方法は、ストアを durable にするか、`IStorageDurabilityDescriptorProvider` を実装して自分が何であるかを申告させるかのどちらかです。

**テスト用エグゼキューターを Production で許可するオーバーライドは存在しません。** 既定で無効なのではなく、フラグの奥に隠れているのでもなく、**存在しません**。volatile なストレージは判断であり得ます(キャッシュ的なサービス、使い捨て環境)。しかし本番でのテスト用エグゼキューターは判断ではなく事故です。

有効にしたオーバーライドは、起動バナーに `Warning` レベルで**名前付きで**出力されます。デプロイ設定を読まなくても分かるようにするためです。

### バナー

起動のたびに出力されます(強制なしで報告だけ欲しい場合は `AddSekibanDcbStartupBanner()`。volatile ストアが目的であるローカル開発に向いています)。

```
Sekiban DCB startup. Environment=Production IsProduction=True
  ExecutorType=Sekiban.Dcb.Orleans.OrleansDcbExecutor ExecutorRuntime=DistributedRuntime ExecutorRuntimeName=Orleans
  EventStoreProvider=CosmosDb EventStoreDurability=Durable
  ProjectionStoreProvider=CosmosDb ProjectionStoreDurability=Durable
  Overrides=(none) Enforcing=True
```

**接続文字列は決して出力しません。** すべてのログシンクに秘密情報を漏らすバナーは、防ごうとしている不具合よりも重大な不具合です。

### インメモリスタックの現在の居場所

揮発ストアとインプロセスエグゼキューターは、専用の住処を得ました: `Sekiban.Dcb.Core.Testing` / `Sekiban.Dcb.WithResult.Testing` / `Sekiban.Dcb.WithoutResult.Testing`(名前空間 `Sekiban.Dcb.Testing`)。これらを参照していないプロジェクトは、**新しい `Sekiban.Dcb.Testing` の入口には到達できません** — ランタイムプロジェクトがテスト用エグゼキューターをうっかり手に取ることはできなくなりました。

一方、旧 `Sekiban.Dcb.InMemory` の型は **削除されていません**。ランタイムパッケージ内に public のまま残り、コンパイルでき、挙動も同一で、新しい住処を指す `[Obsolete]` が付いているだけです。したがって、**旧い名前**とランタイムプロジェクトの間にコンパイラは立ちません。そこに立つのは上のディスクリプタと、それに基づいて動くガードです。ガードは**どの名前で入手したかに関わらず、実際に解決されたもの**に対して作用します(インプロセスエグゼキューターは常に拒否、揮発ストレージは既定で拒否・ストレージ専用オーバーライドで許可可能、`Unknown` は常に fail-closed)。旧い名前が消えるのは次のメジャーです。

本番同様に振る舞うローカル開発環境には、単一サイロの localhost Orleans ホストを使ってください: [localhost Orleans](22_localhost_orleans.md)。

### 非推奨になったコンストラクター

`new InMemoryDcbExecutor(domainTypes)` は `[Obsolete]` になりました。**挙動は変わりません。非推奨にしたのはその「沈黙」です。** 使うストアを明示的に渡してください。

```csharp
var executor = new InMemoryDcbExecutor(domainTypes, new InMemoryEventStore());
```

こうすれば、ストアの選択は「誰かが下した判断」となり、その判断を下したコードの中に見えるようになります。

## マテリアライズドビュー用ストレージ

マテリアライズドビューは現在 PostgreSQL 向けに実装されています。

- `Sekiban.Dcb.MaterializedView` : 共通契約と catch-up worker
- `Sekiban.Dcb.MaterializedView.Postgres` : registry、executor、行アクセス、table update
- `Sekiban.Dcb.MaterializedView.Orleans` : Orleans grain 制御と query accessor

これはメインの event store package とは別レイヤーです。現在の PoC では、イベントストアの DB と
マテリアライズドビューの DB を分離した構成も取れます。詳細は [マテリアライズドビュー基礎](20_materialized_view.md)
を参照してください。

## 条件付き（ユニークキー）追記 — SEK-G15

基本の `IEventStore.WriteSerializableEventsAsync` は無条件です。EventId はサーバー生成で、書き込みは常に成立します。「この処理はちょうど1台のホストだけが実行する」— 例えば N 台のホストにまたがる一度きりのマイグレーション — が必要な場合は、**オプションの**条件付き追記コントラクトを使います。完全に追加のみで、`IEventStore`・`ICommandContext`・既存のシリアライズ DTO は一切変更せず、オプトインしないストアの挙動は変わりません。

### コントラクト

- **オプションインターフェース** `IConditionalEventStore.AppendIfUniqueAsync(ConditionalAppendRequest, ...)` — 呼び出し側が指定する冪等キーの下での単一イベント追記。`IStreamingSerializableEventStore` と同様に `is` で機能検出します。これを実装するストアは `IWriteConditionCapabilityProvider` も必ず実装します（アーキテクチャテストで強制。暗黙のデフォルト素通りは存在しません）。
- **アウトカムマシン**（唯一の観測コントラクト）: `Appended` / `AlreadyCommittedSameOperation`（両者とも耐久的な**レシート**を持つ: 勝者の `EventId`・`SortableUniqueId`・オペレーションフィンガープリント）/ `KeyReuseConflict` / `ConditionNotSupported`。
- **エグゼキュータのシーム** — オプトインのみ: 両ファサードに `ExecuteAsync(command, handler, CommandExecutionOptions, ...)` の新オーバーロード（既存の `ExecuteAsync` は不変、`ICommandContext` も不変）、および WASM 境界に新しいバージョン付き `SerializedConditionalCommitRequest` / `CommitSerializableEventConditionallyAsync`（既存の位置指定 `SerializedCommitRequest` は不変）。

### ケーパビリティの解決（実行時解決・フェイルクローズ）

サポート可否は**実行時ケーパビリティディスクリプタ**で、G10 パターンを再利用します — 型名判定は決して行いません。`WriteConditionKind` は種別を区別します（現在は `SingleEventUniqueKey`。`BatchUniqueKey` / `ExpectedPosition` は将来の予約種別）。ディスクリプタはコンテナが実際に構築した**ライブ**インスタンスから解決され、デコレータは伝播します。`HybridEventStore` はホットストアが強制できる内容をそのまま報告し（書き込みはそこに着地する — 自らの権限で昇格しない）、コンポジットは**すべての**下位ストアが対応する種別のみをサポートします。何も言わないストアは何もサポートしません。

対応しない種別への条件付き追記要求は**フェイルクローズ**します。`ConditionNotSupportedException` は、コマンドハンドラの実行前・EventId の採番前・シリアライズ前・いかなるストア呼び出しの前に送出されます。無条件書き込みへの暗黙のデグレードはありません。

### オペレーションフィンガープリントとレシート

オペレーションの同一性だけでは「同じオペレーション」の証明になりません。各クレームは**正規フィンガープリント**を永続化します。これは長さプレフィックス付き・ドメインセパレータ付きの SHA-256 で、次の順で導出します: 導出バージョン、正規化バージョン、ドメインセパレータ、ServiceId、正規化された冪等キー（NFC・トリム・512 UTF-8 バイト以下）、**権威的イベント型 ID**、**正規ペイロード**、イベントのタグ。サーバー生成の EventId/SortableUniqueId は**除外**します — 真のリトライは新しい id を採番しても同一と認識される必要があるためです。ServiceId は同一性の一部なので、あるサービスで取得したキーが別サービスでマッチすることはありません。生のキーはログにも戻り値にも出さず、不透明なフィンガープリントのみが層の外に出ます。導出は**バージョン管理**され（現在は導出 v2 / 正規化 v1）、リテラルダイジェストのゴールデンベクタで固定されています。したがってバージョン・ドメインセパレータ・フィールド順・長さプレフィックス・正規化アルゴリズムのいかなる変更も、意図的でテストを破壊する変更になります。

- **権威的イベント型 ID**: 型は呼び出し側の単純ペイロード名ではなく、ドメインに登録されたイベント型（CLR の `FullName`）で解決します。未登録の型は副作用前にフェイルクローズします。
- **正規ペイロード（サポートする形）**: 生ペイロードを解決済みの型へデシリアライズしてドメインで再シリアライズし、正規 JSON として再出力します — **オブジェクトのキーは再帰的に序数ソート、配列要素の順序は保持、数値・文字列はドメインシリアライザの出力どおり**（Unicode エスケープとプロパティ順は影響せず、`1` と `1.0` は別物）。records/POCO を System.Text.Json（リフレクション/ソースジェネレータ）でシリアライズする場合に安定し、プロパティ宣言順に依存しません。サポートする形は**ドキュメントだけでなくプログラムで強制**されます: ハッシュ前に、ペイロードの*実効* `JsonTypeInfo` グラフを検証します（サイクルガードあり・非破壊・リフレクション/ソースジェネレータ双方の実メタデータを使用）— ルートは JSON オブジェクトであること、コレクションは順序付き型（配列・`List`/`IList`/`IReadOnlyList`・`Collection`/`ReadOnlyCollection`・`ImmutableArray`/`ImmutableList`）、リーフは決定的プリミティブのアローリスト、そして**カスタムコンバータ・非オブジェクト/コンバータ所有型（`JsonTypeInfoKind.None`）・セット・ディクショナリは受け入れません**。それ以外（非決定的出力をしうるコンバータを含む）は (デ)シリアライズ前に拒否され、不安定な指紋や1キーで2つの異なる指紋を生じることはありません。デシリアライズ/正規化できないペイロードも同様にフェイルクローズします。
- **順序付きタグの意味論**: タグは序数ソート（順序非依存）、重複は有意、大文字小文字は区別されます。

アウトカム:

- 同一キー + **同一**フィンガープリント → `AlreadyCommittedSameOperation`。元の勝者のレシートを返し、何も書き込みません。
- 同一キー + **異なる**フィンガープリント → `KeyReuseConflict`。実プロバイダがユニーク制約違反を表出した場合はそのプロバイダ例外を内部原因として保持することがあります。読み取りで発見された衝突にはプロバイダ例外がなく、捏造もしません。
- 正規化できない場合（未登録型・デシリアライズ不能ペイロード）→ 型付き `OperationCanonicalizationException` でフェイルクローズします。この失敗は**シークレットセーフ**です: 原因となったコンバータ/デシリアライザ例外（メッセージ・`Data`・スタックに生ペイロードやキーを含みうる）は**破棄**し、結果グラフに連結しません。型付き例外はサニタイズ済みメタデータ（登録済みイベント型名）のみを持ち、内部例外もペイロード/キーも持ちません。

### 境界: 耐久クレームは1つ、副作用のちょうど1回ではない

ストアが保証するのは**キーごとに高々1つの耐久クレーム**です。これはストレージの保証であって、副作用のちょうど1回保証ではありません。外部への副作用（メール送信・API 呼び出し）をちょうど1回にするには、勝者クレームの上にアウトボックス／冪等層が別途必要です。

### リファレンス実装とプロバイダの状況

決定論的なインメモリのリファレンスがテスト用パッケージにあり（`Sekiban.Dcb.Testing.InMemoryConditionalEventStore`、ランタイムプロジェクトからは参照しない）、アウトカムマシン全体を実装します。**4つの本番プロバイダすべてが SEK-G16 で実装済み**です — PostgreSQL・SQLite・Cosmos DB・DynamoDB — で、観測可能なセマンティクスは同一です。expected-position / CAS セマンティクスはさらに後のスライスのままです。

### プロバイダの仕組み（SEK-G16）

すべてのプロバイダは、共有オーケストレーター（`ConditionalAppendExecution`）を通じて同一のアウトカムマシンを生成します。プロバイダが提供するのは3つのプリミティブのみ — ネイティブの一意性プリミティブを使ってクレームイベントを決定論的 id の下に耐久書き込みすること、コミット済みの勝者を読み戻すこと、そして（イベント行とインデックス行がアトミックに書かれない場合に）勝者の契約上のコミット済み状態を収束させること — であり、正規化・書き込み前フィンガープリント（未対応形状は**あらゆるストア呼び出しの前に**フェイルクローズ）・分類・コミット済み状態ゲート・レシート構築はオーケストレーターが行います。

**決定論的なストレージ識別子。** クレームイベントは、キーのみから導出した EventId の下に格納されます: `EventId = SHA-256(ドメインセパレータ ‖ バージョン ‖ ServiceId ‖ 正規化キー)` に UUID の version/variant ビットを適用したもの（`ConditionalAppendIdentity`、導出 v1）。呼び出し側のランダムな EventId は格納クレームでは破棄されるため、ストレージ識別子は `(ServiceId, key)` の純粋関数となり、既存の行単位／アイテム単位の主キーがそのまま一意性プリミティブになります — **どのプロバイダでもスキーマ移行なし、新しいカラム／インデックスなし**。識別子は保存ではなく導出され、フィンガープリントは永続化されたイベント内容から再計算されるため、同一操作のリトライは格納済み勝者から同一のフィンガープリントを再計算し、同一キー下の異なる操作は異なるフィンガープリントを再計算します。

**分類は再計算したフィンガープリントによって行い、生の衝突シグナルでは行いません。しかも、衝突は*意図した*ものでなければなりません。** プロバイダの衝突は、それ単体では*決して*同一操作の成功として扱いません。各プロバイダは、決定論的クレーム衝突である特定の制約／理由のみをマッピングします。無関係な制約や想定外のキャンセル理由は元のプロバイダ失敗を保持し、勝者分類へ誤ルーティングされることはありません。マッピングされた衝突時、オーケストレーターはコミット済み勝者を読み戻してフィンガープリントを比較します。勝者を読み戻せない場合（in-doubt／未コミットのクレーム）は、`AlreadyCommittedSameOperation` を報告せず、型付きでリトライ可能な `ConditionalAppendInDoubtException` を送出します。実プロバイダ例外は、実際に発生したときのみ `KeyReuseConflict`（または in-doubt）の診断用内部原因として保持します。

| プロバイダ | 一意性プリミティブ | マッピングする衝突シグナル（これのみがクレーム衝突） | 勝者の読み戻し | コミット済み状態ゲート |
|----------|----------------------|--------------------------------------------------|------------------|----------------------|
| PostgreSQL | `(ServiceId, Id)` 主キー。プレーンなトランザクション（リトライ用の実行戦略ではない）、イベント行＋タグ行を1トランザクション | `DbUpdateException` → `PostgresException` SQLSTATE 23505 **かつ `ConstraintName == "PK_dcb_events"`**（他の 23505 はプロバイダ失敗のまま） | `(ServiceId, Id)` による `AsNoTracking` ポイントリード | 不要（書き込みがアトミック） |
| SQLite | `(ServiceId, Id)` 主キー。新パスは書き込みロック下のプレーンな `INSERT`（レガシーの `INSERT OR REPLACE` は不変）、イベント行＋タグ行を1トランザクション | 隔離したイベント INSERT での **`SqliteExtendedErrorCode == 1555`（SQLITE_CONSTRAINT_PRIMARYKEY）**（他の制約はロールバックして伝播） | `(ServiceId, Id)` によるポイントリード | 不要（書き込みがアトミック） |
| Cosmos DB | パーティション `{serviceId}|{id}` 内のアイテム id。イベントドキュメントとタグ行は**別フェーズ**で書き込む（非アトミック） | イベント作成での `CosmosException` 409 Conflict | イベントアイテムの整合ポイントリード | **必須** — 同一操作のリトライは、AlreadyCommitted の前にすべてのタグ行を冪等に修復／検証する（create → 409 で読み戻し → `ContentEquals`）。コミット済み状態に到達できなければ型付き in-doubt |
| DynamoDB | アイテム主キー `pk = SERVICE#{serviceId}#EVENT#{id}`。**イベント Put（index 0、`attribute_not_exists(pk)`）＋タグ行1件ごとの Put を1つの `TransactWriteItems`** に含める。**`ClientRequestToken` なし**（同一操作のリトライが冪等性で潰されず実際の衝突を表出させるため）。制限は呼び出し前にフェイルクローズで強制：≤100 アイテム・アイテムキー重複なし（`DynamoConditionalAppendLimitException`）。 | `ConditionalCheckFailedException`、または**キャンセル理由の index 0** が `ConditionalCheckFailed` の `TransactionCanceledException`（他の index の理由はプロバイダ失敗） | `ConsistentRead = true` の `GetItem` | 不要（トランザクションがアトミック） |

無条件書き込みパス、`IEventStore`、およびすべてのデフォルトは4プロバイダすべてで不変です。条件付きパスは純粋に追加のみで、ストアと同時に登録されます（`IConditionalEventStore` と `IWriteConditionCapabilityProvider` は、コンテナが既に構築する同一シングルトンに解決されます）。サービス別ファクトリー（`PostgresEventStoreFactory`・`CosmosDbEventStoreFactory`）と `HybridEventStore` デコレーターはこのケイパビリティを伝播します。コンポジットは、すべての参加ストアが対応する場合にのみそのカインドを報告します（フェイルクローズな積集合）。

**アトミック性はプロバイダごとに異なり、一律ではありません。** Postgres/SQLite/DynamoDB はイベントとタグ行を単一トランザクションで書くため、コミット済みイベントには必ずタグ行があります。Cosmos は異なり、イベントドキュメント→タグ行の順で書くため、その間のクラッシュはタグスコープの可視性が未回復のコミット済みイベントを残します。Cosmos のコミット済み状態ゲートは、次の同一操作の試行でまさにこのウィンドウを閉じます。共有アウトカムテストの合格だけでは Cosmos のアトミック性は証明されません。

**型付きでリトライ可能な in-doubt と、リトライ不可な corruption。** 結果を確定できない場合 — 読み戻せる勝者のない衝突、耐久コミットの可能性がある後のキャンセル／タイムアウト、または検証／修復できなかった（一時的な）コミット済み状態 — 追記は型付き `ConditionalAppendInDoubtException`（`IsRetryable == true`、閉じた `Reason` 列挙 — `WinnerUnreadableAfterConflict` / `AmbiguousAfterWrite` / `CommittedStateUnverified` — と安定した文字列 `ReasonCode`、プロバイダ名、ServiceId、*導出* EventId。生キー／ペイロードは持たない）で失敗します。これは第5のアウトカムステータスではなく、呼び出し側がリトライすべき失敗です（勝者がコミットすれば `AlreadyCommittedSameOperation` に収束）。これとは別に、同一操作のリトライが、イベントと**不一致**の既存コミット済みインデックス／タグ行（欠損ではなく厳密な内容不一致）を見つけた場合は `ConditionalAppendCommittedStateCorruptionException`（`IsRetryable == false`）で失敗します。不一致行は**決して上書きせず**、無限にリタイしてはいけません。オペレーターの調査のために表出されます。欠損行は修復し、不一致行は corruption です。WithResult では両者とも `ResultBox.Error`、WithoutResult ではガード境界が再送出し、バージョン付きシリアライズ境界は型付き例外をそのまま保持します（汎用ラップなし）。耐久コミット後のキャンセル／タイムアウトは、まず権威的な読み取り＋フィンガープリント（＋コミット済み状態検証）で可能なら `AlreadyCommittedSameOperation` に解決し、それ以外の場合のみ in-doubt として表出します。理由コードは閉じた集合であり、呼び出し側が任意の理由を構築することはできません。

### レシピ: N ホストにファンアウトした一度きりのマイグレーション

代表的なユースケース: N 個のレプリカが起動し、それぞれが同じ一度きりのマイグレーションを実行しようとするが、勝者はちょうど1つでなければならない。

1. 各ホストは*同一*の `ConditionalAppendRequest` を組み立てます。マイグレーションを表す安定した `IdempotencyKey`（例: `"migration:2026-07-add-region-tag"`）と、単一のマイグレーションマーカーイベント（全ホストで同一のペイロード＋タグ）。
2. 各ホストが `AppendIfUniqueAsync` を呼びます。ちょうど1つが `Appended` を得て、他はすべて勝者のレシートを伴う `AlreadyCommittedSameOperation` を得ます。どちらも成功であり、`AlreadyCommittedSameOperation` を見たホストはマイグレーションが既に耐久的にクレームされたと分かり、ノーオペとして続行します。
3. リトライ可能なエラー（in-doubt クレーム、ストアの一時エラー）を返されたホストは単純にリトライします。リトライは収束します — 勝者がコミットすれば、リトライは `AlreadyCommittedSameOperation` に分類されます。
4. あるホストが同一キー下で*異なる*操作（異なるペイロード／タグ）を組み立てた場合は `KeyReuseConflict` を得ます — 静かにマージされるのではなく、プログラミングエラーとして大きく表出されます。

**境界は耐久クレーム1つ。** 本コントラクトはキーごとに高々1つの耐久クレームを保証しますが、マイグレーションの副作用をちょうど1回にはしません。マイグレーション自体が外部副作用（他システムへの書き込み、通知送信）を行う場合は、それらを勝者クレームの背後にアウトボックス／冪等層で置いてください — クレームが伝えるのは*誰が勝ったか*であって、副作用がちょうど1回実行されたことではありません。

## SEK-G20 generation-aware checkpoint CAS

**バージョン: 10.8.0(マイナー)。** SEK-G18 のクラスタ間・共有ストアの穴を解消します
([よくある問題 → クラスタ間・共有ストア収束](13_common_issues.md) 参照)。マルチプロジェクションの
チェックポイントストア(`IMultiProjectionStateStore`)上の**任意**の capability で、G15/G16 の
conditional-append と同じく LIVE インスタンスから feature-detect します——ストアは
`IGenerationAwareCheckpointStore` を実装し、`DescribeCheckpointCapability()` で
`CheckpointCapabilityKind.GenerationTombstoneCas` を返すことで宣言します。`IMultiProjectionStateStore`
にメンバ追加はなく、positional records(`MultiProjectionStateRecord` / `MultiProjectionStateWriteRequest`)
にもフィールド追加はありません。

**2 層 CAS。** 各チェックポイント行は **generation**(rebuild epoch)と**不透明な per-mutation トークン**
——exact-CAS の revision(Postgres/SQLite: `Revision` 列、Cosmos: `_etag`、DynamoDB: `revision` 属性)——
に加え **lifecycle**(Active / Tombstoned)を保持します。すべての条件付き操作は正確なトークンを比較し、
generation のみの比較は CAS ではありません。固定の状態機械は
`Active(g,rev) → CAS Invalidate → Tombstoned(g+1,rev') → CAS CommitRebuilt → Active(g+1,new rev)` で、
rebuilt ペイロードのコミットと tombstone クリアは同一行の 1 CAS です。

**何を守るか。** retrograde 完全再構築は delete ベースの無効化を耐久的な bump+tombstone に置き換え、
stale peer の後続 persist は `ConditionRejected` となり行を再汚染しません。fresh な活性化はペイロード
束縛の前に制御面を読み、tombstone なら完全な順序付き再生を強制します。すべての product チェックポイント
変更が surface を経由します(無条件 write/delete のバイパスなし)。

| プロバイダ | exact-token プリミティブ | スキーマ更新 |
|-----------|------------------------|-------------|
| Postgres | 条件付き `UPDATE … WHERE Generation=@g AND Revision=@r AND Lifecycle=@l`(行数) | 加算 EF migration(`Generation`/`Revision`/`Lifecycle`、既定 0) |
| SQLite | 条件付き `UPDATE`(行数) | 加算 `ALTER TABLE … ADD COLUMN … DEFAULT 0` |
| Cosmos | `ReplaceItem` の `IfMatchEtag`(412 → 拒否) | `generation`/`lifecycle` プロパティ(欠損 → 0) |
| DynamoDB | 条件付き `PutItem`/`UpdateItem` の `ConditionExpression` | `generation`/`revision`/`lifecycle` 属性(欠損 → 0) |

**互換性。** 既存行は **generation 0 / revision 0 / Active** として読まれます——イベント/ペイロードの移行は
なく、加算のチェックポイントスキーマ更新のみが必要で、プロバイダごとに実 pre-G20 DB で検証済みです。
スキーマ未適用の間、capability 操作はフェイルクローズします(黙ってレガシーへフォールバックしません)。
capability を実装しないストアはレガシーの無条件書き込みをバイト単位で維持し、それに対する retrograde
無効化は cross-cluster の stale 再汚染を避けるため **G14 fault パスへフェイルクローズ**します(operator
reset 必要)。

**mixed-version の注意(デプロイごとに要記載)。** 保護が完成するのは全 WRITER・READER がアップグレード
された時のみです。**SQLite** ではレガシーの `INSERT OR REPLACE` upsert が行を delete+reinsert して制御列を
リセットするため、pre-G20 の writer が tombstone を消します。cross-cluster 保証に依存する前に全クラスタ/
writer を 10.8.0 へ更新してください。

## 関連資料

現在のインターナルユースで使っているコールドイベントの書き出し、ハイブリッドリード、キャッチアップワーカー構成については [コールドイベントとキャッチアップ](19_cold_events.md) を参照してください。
