# Issue 876: DynamoDB Event Store 設計案（Codex）

## 0. 前提 / 目的

### Issue 876 要件（gh issue 準拠）
- **Summary**: Sekiban.Dcb に DynamoDB を追加（Cosmos DB 実装と同じコンセプト）
- **Acceptance Criteria**:
  - DynamoDB event store 実装
  - Cosmos DB provider と同じ API サーフェス
  - DynamoDB セットアップ/設定のドキュメント

### 本設計の目的
- `Sekiban.Dcb` のイベントストアを DynamoDB で動作させる。
- Cosmos DB 実装と同じアーキテクチャ / API パターンを維持する。
- **アクセスパターン / テーブル設計 / 取引制限 / 失敗時挙動**を明示する。

---

## 1. スコープ

### In Scope
- DynamoDB 実装の `IEventStore`（Read/Write/Tag/Count）
- DI 拡張: `AddSekibanDcbDynamoDb(...)`
- DynamoDB テーブル設計
- 書き込みの原子性/ロールバック方針
- ドキュメント（設定・接続文字列/資格情報の説明）
- **（任意）** `IMultiProjectionStateStore` の DynamoDB 版

### Out of Scope
- 既存 Cosmos/Postgres 実装のリファクタ
- 既存データ移行ツールの実装
- 本設計の範囲外となる周辺サービス（Dapr / Orleans）

---

## 2. 既存前提（ソースから読み取れる仕様）

- `IEventStore` は**イベントとタグの同時書き込み**を要求する（強整合とは限らない）。
- `SortableUniqueId` による**全体順序**・タグ内順序が必要。
- `ReadEventsByTagAsync` はタグ→イベントID→イベント実体の順で取得するのが既存（Cosmos）。
- `GetAllTagsAsync` は**全タグ情報**（件数/最初/最後）を返す。
- Cosmos DB provider は `events` / `tags` / `multiProjectionStates` の 3 コンテナ構成。
- Cosmos DB provider には `AddSekibanDcbCosmosDb(...)` / `AddSekibanDcbCosmosDbWithAspire(...)` が存在し、
  DynamoDB 版も同等の DI 体験を提供するのが要件。

---

## 3. DynamoDB テーブル設計（案）

### 3.1 Events テーブル
**用途**: イベント本体の保存 + 全イベント順序の取得。
Cosmos の `events` コンテナと同じ責務を持つ。

| 属性 | 型 | 役割 |
| --- | --- | --- |
| `id` | S | **PK** (eventId) |
| `sortableUniqueId` | S | イベント順序キー |
| `eventType` | S | 型名 |
| `payload` | S | JSON |
| `tags` | L/S | タグ一覧 |
| `timestamp` | S | UTC | 
| `causationId` | S | メタ |
| `correlationId` | S | メタ |
| `executedUser` | S | メタ |
| `gsi1pk` | S | **GSI1 PK** = "all" 固定 |
| `gsi1sk` | S | **GSI1 SK** = `sortableUniqueId` |

**GSI1**: `gsi1pk` + `gsi1sk`（全イベントの順序取得 / Count 用）

---

### 3.2 Tags テーブル
**用途**: タグ→イベントIDのストリーム。
Cosmos の `tags` コンテナと同じ責務を持つ。

| 属性 | 型 | 役割 |
| --- | --- | --- |
| `tag` | S | **PK** |
| `sortableUniqueId` | S | **SK** |
| `eventId` | S | 参照 |
| `eventType` | S | 参照 |
| `tagGroup` | S | 集計用 |
| `createdAt` | S | UTC |
| `itemType` | S | `stream` 固定（後述の summary と区別） |

**補足**: `SortableUniqueId` は**ユニークである前提**。もし将来競合が起きるなら
`sortableUniqueId#eventId` を SK として採用する（要調整）。

---

### 3.3 TagSummary（オプション）
`GetAllTagsAsync` を Scan で毎回集計するのは重い。そこで**タグ集計アイテム**を導入。

**実装パターン**:
- **同一テーブル混在**: Tags テーブル内に `itemType = summary` を追加
  - PK = `tag`, SK = `summary`
  - Attributes: `eventCount`, `firstSortableUniqueId`, `lastSortableUniqueId`, `firstEventAt`, `lastEventAt`, `tagGroup`

**GSI2（任意）**: `tagGroup` + `tag` で group 絞り込みを高速化。

---

### 3.4 MultiProjectionStates テーブル（任意）
DynamoDB の `MultiProjectionStates` は Cosmos の `multiProjectionStates` コンテナに対応する。
Acceptance Criteria には明記されていないが、**API サーフェス同等化**の観点で
DynamoDB 版 `IMultiProjectionStateStore` を用意する案を提示する。

| 属性 | 型 | 役割 |
| --- | --- | --- |
| `partitionKey` | S | **PK** = `MultiProjectionState_{ProjectorName}` |
| `sortKey` | S | **SK** = `ProjectorVersion` |
| `eventsProcessed` | N | 最新判定 |
| `stateData` | B | 圧縮済み byte[] |
| その他 |  | `MultiProjectionStateRecord` を保存 |

**最新取得**は以下のいずれか:
- **GSI**: PK=`partitionKey`, SK=`eventsProcessed`
- **または** `latest` ポインタアイテムを SK=`latest` で保持（更新時に上書き）

---

## 4. アクセスパターン / API 対応

### 4.1 ReadAllEventsAsync(since)
- Events 테ーブルの **GSI1** を Query
- `gsi1pk = "all"` かつ `gsi1sk > since`
- GSI は**Eventually Consistent**なので読み取り遅延あり

### 4.2 ReadEventsByTagAsync(tag, since)
1. Tags テーブルを `PK = tag` で Query
2. `sortableUniqueId > since` を条件に SK フィルタ
3. 取得した `eventId` を BatchGet で Events テーブルから取得
4. 結果は **Tag Query の順序**で並び替える

### 4.3 ReadEventAsync(eventId)
- Events テーブルを `GetItem` (PK = eventId)

### 4.4 WriteEventsAsync(events)
- **items <= 25** なら `TransactWriteItems` でイベント + タグ +（任意）サマリ更新まで同時実行
- **items > 25** なら以下の best-effort:
  1) Events を `BatchWrite`（25件単位）
  2) Tags を `BatchWrite`
  3) Tags 失敗時は Options に応じて Events を削除（rollback）

### 4.5 ReadTagsAsync(tag)
- Tags テーブル `PK = tag` を Query
- `itemType = stream` のみ取得

### 4.6 GetLatestTagAsync(tag)
- Tags テーブル `PK = tag` Query, `ScanIndexForward = false`, `Limit = 1`

### 4.7 TagExistsAsync(tag)
- Tags テーブル `PK = tag`, `Limit = 1`

### 4.8 GetEventCountAsync(since)
- Events テーブル GSI1 で `Select = COUNT`

### 4.9 GetAllTagsAsync(tagGroup)
- **ベストエフォート方式**:
  - Tags テーブルを Scan し集計
  - 大規模データでは高コスト
- **推奨**:
  - TagSummary を利用して `tagGroup` で Query

---

## 5. 書き込みの原子性/整合性

- DynamoDB の `TransactWriteItems` は **25 アイテム制限**。
- Cosmos 実装と同様、**大きいバッチは best-effort + rollback** で対応。
- TagSummary も完全原子性を求めず、**WriteEvents 成功後の更新**（best-effort）を許容。

**補足**: DCB の整合性境界は TagConsistentActor 側で担保されるため、
イベントの順序保証さえ満たせば DynamoDB の eventual consistency を許容。

---

## 6. API / DI / Options

### 6.1 新規プロジェクト
`dcb/src/Sekiban.Dcb.DynamoDb/`

### 6.2 主要クラス
- `DynamoDbContext`
  - テーブル名解決、クライアント保持、必要なら CreateTable
- `DynamoDbEventStore : IEventStore`
- `DynamoDbEventStoreOptions`
  - TablePrefix, UseTransactions, TryRollbackOnFailure, MaxBatchSize, MaxRetry, ConsistentRead etc.
- （任意）`DynamoDbMultiProjectionStateStore : IMultiProjectionStateStore`

### 6.3 DI 拡張
```csharp
services.AddSekibanDcbDynamoDb(configuration);
// or
services.AddSekibanDcbDynamoDb(dynamoDbClient, options => { ... });
```

### 6.4 API サーフェス（Cosmos DB provider との整合）
Cosmos DB provider と同じ「使い勝手」を担保するため、以下の方針を採用する。

- **拡張メソッドの粒度**は Cosmos と同等（IEventStore 登録が中心）。
- **IConfiguration ベースの登録**を用意（Cosmos の `AddSekibanDcbCosmosDb(IConfiguration)` 相当）。
- **直接クライアントを渡す登録**を用意（Cosmos の connection string overload 相当）。
- Aspire 相当の DI 連携が必要なら、`IAmazonDynamoDB` を DI から取得する
  `AddSekibanDcbDynamoDbWithAspire()` を用意（Cosmos の WithAspire パターンと同型）。

> `IMultiProjectionStateStore` は Cosmos provider でも標準登録されていないため、
> DynamoDB 版も**標準では登録しない**方針を推奨。必要なら専用の拡張メソッドで opt‑in する。

---

## 7. 失敗時挙動

- `BatchWrite` では **UnprocessedItems** が返るため、指数バックオフで再試行。
- 失敗時には ResultBox.Error で例外を返却。
- rollback に失敗した場合は log 警告のみ。

---

## 8. テスト計画（設計レベル）

- **Read/Write 基本**: InMemory/Postgres のテストケースを Dynamo で再現
- **WriteEventsAsync**
  - 25 以下: TransactWrite 成功
  - 25 超: BatchWrite + rollback 挙動
- **ReadEventsByTag**
  - since フィルタ
  - eventId 欠損時の error
- **GetAllTagsAsync**
  - TagSummary 利用 vs Scan

ローカル実行には `DynamoDB Local` or `LocalStack` を使用。

---

## 9. ドキュメント計画（Acceptance Criteria 対応）

- 追加先候補:
  - `docs/docfx_project/articles/prepare-dynamodb.md`
  - `docs/docfx_project/articles_ja/prepare-dynamodb.md`
- 記載内容:
  - 事前準備（AWS credentials / ローカル DynamoDB）
  - 必要テーブルと GSI
  - `AddSekibanDcbDynamoDb(...)` の設定例
  - 代表的な appsettings 設定例
- `docs/docfx_project/toc.yml` に新規ページを追加

---

## 10. 既存コードとの整合点

- Cosmos 実装と同様に**タグ分離**（Tags テーブル）を保持する。
- `TagWriteResult.Version` は Cosmos と同様に `1` を返すか、
  TagSummary の `eventCount` を返せる場合はそれを返す。

---

## 11. Open Questions / 確認事項

1. `SortableUniqueId` は**タグ単位でも必ずユニーク**か？
2. `GetAllTagsAsync` の性能要求は？（Scan を許容できるか）
3. MultiProjectionStateStore を DynamoDB でも提供する必要があるか？
4. テーブル作成をアプリ起動時に自動で行うか？（本番運用での許容）
5. ドキュメントの配置先は docfx で問題ないか？（README 追記の要否）

---

## 12. まとめ

- DynamoDB を追加する場合、**Events テーブル + Tags テーブル**を分離し、
  **GSI で全イベント順序を提供**する構成が最もシンプル。
- DynamoDB の **Transaction 25 件制限**が最大の設計制約。
  小規模は原子性確保、大規模は best-effort + rollback で Cosmos と一致させる。
- `GetAllTagsAsync` は TagSummary を導入しないと高コスト。
  導入の有無を issue 要件で決める。
