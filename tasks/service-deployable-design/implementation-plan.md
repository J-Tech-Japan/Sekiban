# ServiceId マルチテナント実装タスク分解

## 目的
設計ドキュメントに基づき、ServiceId でのマルチテナント隔離を段階的に実装する。

## フェーズとタスク

### Phase 1: Core (P0)
- [ ] `IServiceIdProvider` と Provider 実装（Default/Fixed/Jwt/Required）
- [ ] ServiceId 検証・正規化（`^[a-z0-9-]{1,64}$`、小文字化）
- [ ] `IEventStoreFactory` / `IMultiProjectionStateStoreFactory` 追加

### Phase 2: Cosmos DB (P1)
- [ ] Cosmos モデルに `pk` と `serviceId` 追加
- [ ] `ICosmosContainerResolver` 追加（`default` は legacy、それ以外は v2）
- [ ] `CosmosDbEventStore` の ServiceId フィルタと PK 生成
- [ ] `CosmosDbMultiProjectionStateStore` の ServiceId 対応
- [ ] SQL のパラメータ化 (`QueryDefinition`)
- [ ] コンテナ作成時の `/pk` パスと複合インデックス

### Phase 3: Postgres (P1)
- [ ] `service_id` カラム追加 + 既存データに default
- [ ] PK/UNIQUE に `service_id` を含める
- [ ] クエリに ServiceId フィルタ追加
- [ ] `PostgresEventStoreFactory` 実装

### Phase 4: DI・運用 (P2)
- [ ] DI 拡張メソッド（Single / HTTP / Orleans / Full）
- [ ] `RequiredServiceIdProvider` を non-HTTP の既定に
- [ ] ServiceId をログ/メトリクスに含める

### Phase 5: テスト/移行 (P2)
- [ ] Provider・検証の単体テスト
- [ ] テナント分離の結合テスト
- [ ] Cosmos 移行ツール/手順の整備
- [ ] Postgres 制約移行のテスト

## 進捗
- Phase 1 を着手中。
