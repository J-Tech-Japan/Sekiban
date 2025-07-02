# Sekiban.Pure TypeScript化計画書

## 概要

Sekiban.PureをTypeScriptに移植し、npmパッケージとして公開するための計画書です。
C#で実装されているイベントソーシング・CQRSフレームワークをTypeScript/JavaScript環境で利用可能にします。

## パッケージ名の検討

調査の結果、以下のnpmパッケージ名は現時点で利用可能です：

- `sekiban` - シンプルで覚えやすい
- `sekiban-pure` - C#版との対応が明確
- `@sekiban/core` - スコープ付きパッケージ（推奨）
- `sekiban-es` - Event Sourcingを強調

**推奨案**: `@sekiban/core`をメインパッケージとし、関連パッケージを`@sekiban/*`スコープで管理

## パッケージ構成

```
@sekiban/core          - コア機能（Sekiban.Pure相当）
@sekiban/dapr          - Dapr統合（Sekiban.Pure.Dapr相当）
@sekiban/testing       - テストユーティリティ
@sekiban/cosmos        - Azure Cosmos DB統合
@sekiban/postgres      - PostgreSQL統合
```

## アーキテクチャ設計

### 1. コアコンセプトの移植

#### Aggregate（集約）
```typescript
interface IAggregatePayload {
  // 集約の状態を表すインターフェース
}

interface IAggregate<TPayload extends IAggregatePayload> {
  payload: TPayload;
  version: number;
  partitionKeys: PartitionKeys;
}
```

#### Event（イベント）
```typescript
interface IEventPayload {
  // イベントデータを表すインターフェース
}

interface IEvent<TPayload extends IEventPayload> {
  aggregateId: string;
  payload: TPayload;
  sortableUniqueId: string;
  createdAt: Date;
}
```

#### Command（コマンド）
```typescript
interface ICommand {
  // コマンドの基本インターフェース
}

interface ICommandWithHandler<TCommand extends ICommand, TProjector, TPayload = any> {
  // ハンドラー付きコマンド
}
```

#### Projector（プロジェクター）
```typescript
abstract class AggregateProjector<TPayload extends IAggregatePayload> {
  abstract applyEvent(payload: TPayload | undefined, event: IEventPayload): TPayload;
  abstract getInitialPayload(): TPayload;
}
```

### 2. 型システムの対応

| C#の機能 | TypeScriptでの実装方法 |
|---------|---------------------|
| record型 | readonly interfaceまたはclass |
| 属性（Attributes） | デコレーター |
| ジェネリック制約 | TypeScriptのジェネリック制約 |
| パターンマッチング | Type GuardsとDiscriminated Unions |
| null許容参照型 | strictNullChecksとoptional |

### 3. 依存ライブラリの選定

#### Result型の実装
C#のResultBoxに相当する機能：
- **Option 1**: `neverthrow` - 人気のあるResult/Either実装
- **Option 2**: カスタム実装で軽量化
- **推奨**: カスタム実装（フレームワークに最適化）

#### シリアライゼーション
- **class-transformer**: オブジェクトとクラスの相互変換
- **class-validator**: バリデーション
- **superjson**: Date型などの特殊な型をサポート

#### その他の依存関係
- **uuid**: UUID生成
- **date-fns**: 日付操作
- **zod**: スキーマバリデーション（オプション）

## 実装計画

### フェーズ1: コア機能実装（4週間）

1. **基本型定義**（1週目）
   - インターフェース定義
   - 基本的な型システム
   - Result型の実装

2. **インメモリ実装**（2週目）
   - InMemoryEventStore
   - InMemorySekibanExecutor
   - 基本的なコマンド実行

3. **プロジェクターシステム**（3週目）
   - AggregateProjector基底クラス
   - イベント適用ロジック
   - スナップショット機能

4. **クエリシステム**（4週目）
   - Query/ListQueryインターフェース
   - ページング機能
   - フィルタリング

### フェーズ2: 高度な機能（3週間）

1. **マルチプロジェクション**（1週目）
   - 複数集約の横断クエリ
   - プロジェクション定義

2. **バリデーション・エラーハンドリング**（2週目）
   - コマンドバリデーション
   - ビジネスルール検証
   - エラー型の定義

3. **型登録システム**（3週目）
   - ドメイン型の自動登録
   - デコレーターベースの登録
   - メタデータ管理

### フェーズ3: Dapr統合（4週間）

1. **Dapr SDK統合**（1週目）
   - Dapr Actor基本実装
   - 状態管理

2. **分散イベントストア**（2週目）
   - Actor経由のイベント永続化
   - イベントストリーム管理

3. **Pub/Sub統合**（3週目）
   - イベント配信
   - サブスクリプション管理

4. **Protobufサポート**（4週目）
   - プロトコル定義
   - シリアライゼーション最適化

### フェーズ4: 品質向上（2週間）

1. **テスト整備**
   - ユニットテスト
   - 統合テスト
   - E2Eテスト

2. **ドキュメント作成**
   - APIドキュメント
   - 使用例
   - マイグレーションガイド

## プロジェクト構造

```
sekiban-ts/
├── packages/
│   ├── core/
│   │   ├── src/
│   │   │   ├── aggregates/
│   │   │   │   ├── types.ts
│   │   │   │   ├── projector.ts
│   │   │   │   └── index.ts
│   │   │   ├── commands/
│   │   │   │   ├── types.ts
│   │   │   │   ├── handler.ts
│   │   │   │   └── index.ts
│   │   │   ├── events/
│   │   │   │   ├── types.ts
│   │   │   │   ├── store.ts
│   │   │   │   └── index.ts
│   │   │   ├── queries/
│   │   │   │   ├── types.ts
│   │   │   │   ├── handler.ts
│   │   │   │   └── index.ts
│   │   │   ├── documents/
│   │   │   │   ├── partition-keys.ts
│   │   │   │   ├── sortable-unique-id.ts
│   │   │   │   └── index.ts
│   │   │   ├── executors/
│   │   │   │   ├── types.ts
│   │   │   │   ├── in-memory.ts
│   │   │   │   └── index.ts
│   │   │   ├── serialization/
│   │   │   │   ├── json.ts
│   │   │   │   ├── types.ts
│   │   │   │   └── index.ts
│   │   │   ├── result/
│   │   │   │   ├── result-box.ts
│   │   │   │   ├── operators.ts
│   │   │   │   └── index.ts
│   │   │   └── index.ts
│   │   ├── tests/
│   │   ├── package.json
│   │   ├── tsconfig.json
│   │   └── README.md
│   │
│   ├── dapr/
│   │   ├── src/
│   │   │   ├── actors/
│   │   │   ├── event-store/
│   │   │   ├── controllers/
│   │   │   └── index.ts
│   │   ├── protos/
│   │   ├── package.json
│   │   └── tsconfig.json
│   │
│   └── testing/
│       ├── src/
│       │   ├── test-base.ts
│       │   ├── fixtures.ts
│       │   └── index.ts
│       ├── package.json
│       └── tsconfig.json
│
├── examples/
│   ├── basic-usage/
│   ├── with-dapr/
│   └── migration-from-csharp/
│
├── docs/
│   ├── api/
│   ├── guides/
│   └── migration/
│
├── scripts/
├── .github/
│   └── workflows/
├── pnpm-workspace.yaml
├── package.json
├── tsconfig.base.json
└── README.md
```

## 技術選定の詳細

### ビルドツール
- **Vite**: 高速な開発体験とライブラリビルド
- **tsup**: シンプルなライブラリバンドル（代替案）

### パッケージマネージャー
- **pnpm**: ワークスペース機能とディスク効率
- スコープ付きパッケージの管理が容易

### テストフレームワーク
- **Vitest**: Viteとの統合、高速実行
- **Testing Library**: 統合テスト用

### 開発ツール
- **TypeScript**: 4.9以上（satisfies演算子）
- **ESLint**: コード品質
- **Prettier**: コードフォーマット
- **Changesets**: バージョン管理

## 移行戦略

### C#からTypeScriptへの移行パス

1. **インターフェース互換性**
   - C#版と同じ概念を維持
   - 命名規則の統一

2. **段階的移行サポート**
   - C#版との相互運用（イベントストア経由）
   - マイグレーションツールの提供

3. **ドキュメントとサンプル**
   - C#開発者向けガイド
   - 対応表の作成
   - 実践的なサンプルコード

## リスクと対策

### 技術的リスク

1. **型安全性の維持**
   - リスク: TypeScriptの構造的型付けによる意図しない型の混同
   - 対策: ブランド型やnominal typingの活用

2. **パフォーマンス**
   - リスク: C#版と比較したパフォーマンス劣化
   - 対策: ベンチマークテストの実施、最適化

3. **Dapr統合の複雑性**
   - リスク: Dapr JavaScript SDKの制限
   - 対策: 段階的な実装、フォールバック戦略

### 組織的リスク

1. **メンテナンス負荷**
   - リスク: C#版とTypeScript版の二重メンテナンス
   - 対策: 共通仕様の文書化、自動化テスト

2. **コミュニティ分断**
   - リスク: 言語別のコミュニティ分断
   - 対策: 統一フォーラム、相互サポート

## 成功指標

1. **機能的完全性**
   - C#版の主要機能の95%以上をカバー
   - 互換性のあるイベントフォーマット

2. **パフォーマンス**
   - 基本操作で100ms以内のレスポンス
   - 1000イベント/秒の処理能力

3. **採用率**
   - 6ヶ月で100以上のnpmダウンロード/週
   - 5つ以上の実プロジェクトでの採用

4. **品質**
   - 90%以上のテストカバレッジ
   - TypeScriptの厳格モード対応

## タイムライン

- **2024年Q1**: フェーズ1完了、アルファ版リリース
- **2024年Q2**: フェーズ2完了、ベータ版リリース
- **2024年Q3**: フェーズ3完了、RC版リリース
- **2024年Q4**: フェーズ4完了、正式版リリース

## まとめ

Sekiban.PureのTypeScript化により、JavaScriptエコシステムでも本格的なイベントソーシング・CQRSフレームワークが利用可能になります。C#版の設計思想を維持しながら、TypeScriptの特性を活かした実装を目指します。