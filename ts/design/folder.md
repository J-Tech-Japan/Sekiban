# Sekiban TypeScript プロジェクト構成

## 概要

このドキュメントでは、Sekiban TypeScript版の`ts/src`配下のフォルダ構成と、各コンポーネントの役割を定義します。
モノレポ構成を採用し、複数のパッケージを効率的に管理・開発できる構造を目指します。

## ディレクトリ構造

```
ts/
├── src/
│   ├── packages/                    # メインパッケージ群
│   │   ├── core/                   # @sekiban/core - コア機能
│   │   ├── dapr/                   # @sekiban/dapr - Dapr統合
│   │   ├── testing/                # @sekiban/testing - テストユーティリティ
│   │   ├── cosmos/                 # @sekiban/cosmos - Azure Cosmos DB統合
│   │   └── postgres/               # @sekiban/postgres - PostgreSQL統合
│   │
│   ├── examples/                   # サンプルプロジェクト
│   │   ├── basic-usage/            # 基本的な使用例
│   │   ├── with-dapr/              # Dapr統合の例
│   │   └── migration-from-csharp/  # C#からの移行例
│   │
│   └── tests/                      # 統合テスト・E2Eテスト
│       ├── integration/            # パッケージ間の統合テスト
│       ├── e2e/                    # エンドツーエンドテスト
│       └── performance/            # パフォーマンステスト
│
├── scripts/                        # ビルド・開発用スクリプト
├── docs/                          # プロジェクトドキュメント
├── design/                        # 設計ドキュメント（このファイルが含まれる）
└── .github/                       # GitHub Actions設定
```

## packages/ - メインパッケージ群

### @sekiban/core

コアとなるイベントソーシング機能を提供するパッケージ。

```
core/
├── aggregates/                     # 集約ルート関連
│   ├── types.ts                   # インターフェース定義
│   ├── projector.ts               # イベント適用ロジック
│   └── index.ts                   # エクスポート
├── commands/                      # コマンド処理
│   ├── types.ts                   # コマンド型定義
│   ├── handler.ts                 # コマンドハンドラー
│   ├── executor.ts                # コマンド実行エンジン
│   └── index.ts
├── events/                        # イベント管理
│   ├── types.ts                   # イベント型定義
│   ├── store.ts                   # イベントストア抽象化
│   ├── stream.ts                  # イベントストリーム
│   └── index.ts
├── queries/                       # クエリ処理
│   ├── types.ts                   # クエリ型定義
│   ├── handler.ts                 # クエリハンドラー
│   ├── projections.ts             # マルチプロジェクション
│   └── index.ts
├── documents/                     # ドキュメント構造
│   ├── partition-keys.ts          # パーティションキー管理
│   ├── sortable-unique-id.ts      # ソート可能な一意ID
│   ├── metadata.ts                # メタデータ定義
│   └── index.ts
├── executors/                     # 実行エンジン
│   ├── types.ts                   # エグゼキューター型
│   ├── in-memory.ts               # インメモリ実装
│   ├── base.ts                    # 基底クラス
│   └── index.ts
├── result/                        # Result型（エラーハンドリング）
│   ├── neverthrow.ts              # neverthrow再エクスポート
│   ├── errors.ts                  # エラー型定義
│   └── index.ts
├── serialization/                 # シリアライゼーション
│   ├── json.ts                    # JSON処理
│   ├── event-format.ts            # イベント形式変換
│   └── index.ts
├── utils/                         # ユーティリティ
│   ├── uuid.ts                    # UUID生成
│   ├── date.ts                    # 日付処理
│   └── index.ts
├── package.json                   # パッケージ設定
├── tsconfig.json                  # TypeScript設定
├── README.md                      # パッケージドキュメント
└── index.ts                       # メインエントリポイント
```

### @sekiban/dapr

Daprアクター統合を提供するパッケージ。

```
dapr/
├── actors/                        # Daprアクター
│   ├── actor-proxy.ts             # アクタープロキシラッパー
│   ├── aggregate-actor.ts         # 集約アクター実装
│   └── types.ts                   # 型定義
├── client/                        # Daprクライアント
│   ├── dapr-client.ts             # クライアント実装
│   └── executor.ts                # Dapr用エグゼキューター
├── serialization/                 # シリアライゼーション
│   ├── compression.ts             # GZip圧縮
│   └── interop.ts                 # C#との相互運用
├── package.json
├── tsconfig.json
└── index.ts
```

### @sekiban/testing

テスト作成を支援するユーティリティパッケージ。

```
testing/
├── fixtures/                      # テストフィクスチャ
│   ├── events.ts                  # イベントフィクスチャ
│   ├── aggregates.ts              # 集約フィクスチャ
│   └── commands.ts                # コマンドフィクスチャ
├── builders/                      # テストデータビルダー
│   ├── event-builder.ts           # イベントビルダー
│   └── aggregate-builder.ts       # 集約ビルダー
├── assertions/                    # カスタムアサーション
│   ├── result-assertions.ts       # Result型アサーション
│   └── event-assertions.ts        # イベントアサーション
├── test-base.ts                   # テスト基底クラス
├── in-memory-test.ts              # インメモリテスト環境
├── package.json
└── index.ts
```

### @sekiban/cosmos

Azure Cosmos DB統合パッケージ。

```
cosmos/
├── client/                        # Cosmos DBクライアント
│   ├── cosmos-client.ts           # クライアント実装
│   └── config.ts                  # 設定
├── store/                         # イベントストア実装
│   ├── cosmos-event-store.ts      # Cosmos DB用イベントストア
│   └── query-builder.ts           # クエリビルダー
├── package.json
└── index.ts
```

### @sekiban/postgres

PostgreSQL統合パッケージ。

```
postgres/
├── client/                        # PostgreSQLクライアント
│   ├── pg-client.ts               # クライアント実装
│   └── pool.ts                    # コネクションプール
├── store/                         # イベントストア実装
│   ├── postgres-event-store.ts    # PostgreSQL用イベントストア
│   └── sql-builder.ts             # SQLビルダー
├── migrations/                    # データベースマイグレーション
│   ├── 001_initial.sql
│   └── migrate.ts
├── package.json
└── index.ts
```

## examples/ - サンプルプロジェクト

### basic-usage

基本的な使用例を示すプロジェクト。

```
basic-usage/
├── src/
│   ├── domain/                    # ドメインモデル
│   │   └── user/                  # ユーザー集約
│   │       ├── commands/          # ユーザーコマンド
│   │       │   ├── create-user.ts
│   │       │   └── update-user.ts
│   │       ├── events/            # ユーザーイベント
│   │       │   ├── user-created.ts
│   │       │   └── user-updated.ts
│   │       ├── projector.ts       # ユーザープロジェクター
│   │       └── queries/           # ユーザークエリ
│   │           └── get-user.ts
│   └── index.ts                   # アプリケーションエントリポイント
├── package.json
├── tsconfig.json
└── README.md
```

### with-dapr

Dapr統合の実装例。

```
with-dapr/
├── src/
│   ├── domain/                    # ドメインモデル
│   ├── actors/                    # Daprアクター設定
│   └── index.ts
├── dapr/                          # Dapr設定ファイル
│   └── config.yaml
├── package.json
└── README.md
```

### migration-from-csharp

C#版からの移行例とガイド。

```
migration-from-csharp/
├── src/
│   ├── csharp-compatible/         # C#互換実装
│   └── typescript-native/         # TypeScriptネイティブ実装
├── docs/
│   ├── mapping-guide.md           # 型マッピングガイド
│   └── migration-steps.md         # 移行手順
├── package.json
└── README.md
```

## tests/ - テストスイート

### integration/

パッケージ間の統合テスト。

```
integration/
├── core/                          # コアパッケージテスト
│   ├── command-execution.test.ts
│   └── event-sourcing.test.ts
├── dapr/                          # Dapr統合テスト
│   └── actor-communication.test.ts
└── interop/                       # C#との相互運用テスト
    └── event-format.test.ts
```

### e2e/

エンドツーエンドのシナリオテスト。

```
e2e/
├── basic-flow.test.ts             # 基本的なワークフロー
├── dapr-flow.test.ts              # Daprワークフロー
└── fixtures/                      # E2Eテスト用フィクスチャ
```

### performance/

パフォーマンステストとベンチマーク。

```
performance/
├── benchmarks.ts                  # ベンチマークスイート
├── load-tests.ts                  # 負荷テスト
└── results/                       # ベンチマーク結果
```

## 設定ファイル

### ルートレベル設定

#### package.json
```json
{
  "name": "sekiban-ts",
  "private": true,
  "workspaces": [
    "src/packages/*",
    "src/examples/*"
  ],
  "scripts": {
    "build": "pnpm -r build",
    "test": "vitest",
    "test:unit": "vitest run --dir src/packages",
    "test:integration": "vitest run --dir src/tests/integration",
    "test:e2e": "vitest run --dir src/tests/e2e",
    "lint": "eslint src/**/*.ts",
    "type-check": "tsc --noEmit",
    "clean": "pnpm -r clean",
    "changeset": "changeset",
    "version": "changeset version",
    "publish": "pnpm build && changeset publish"
  },
  "devDependencies": {
    "@changesets/cli": "^2.27.0",
    "@types/node": "^20.0.0",
    "@typescript-eslint/eslint-plugin": "^6.0.0",
    "@typescript-eslint/parser": "^6.0.0",
    "eslint": "^8.50.0",
    "prettier": "^3.0.0",
    "typescript": "^5.3.0",
    "vitest": "^1.0.0"
  }
}
```

#### pnpm-workspace.yaml
```yaml
packages:
  - 'src/packages/*'
  - 'src/examples/*'
```

#### tsconfig.base.json
```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ESNext",
    "lib": ["ES2022"],
    "moduleResolution": "bundler",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "forceConsistentCasingInFileNames": true,
    "declaration": true,
    "declarationMap": true,
    "sourceMap": true,
    "resolveJsonModule": true,
    "isolatedModules": true,
    "noUncheckedIndexedAccess": true,
    "noImplicitOverride": true,
    "experimentalDecorators": true,
    "emitDecoratorMetadata": true
  }
}
```

### パッケージレベル設定

#### packages/core/package.json
```json
{
  "name": "@sekiban/core",
  "version": "0.1.0",
  "description": "Core event sourcing functionality for Sekiban",
  "main": "dist/index.js",
  "module": "dist/index.mjs",
  "types": "dist/index.d.ts",
  "exports": {
    ".": {
      "types": "./dist/index.d.ts",
      "import": "./dist/index.mjs",
      "require": "./dist/index.js"
    },
    "./aggregates": {
      "types": "./dist/aggregates/index.d.ts",
      "import": "./dist/aggregates/index.mjs",
      "require": "./dist/aggregates/index.js"
    },
    "./commands": {
      "types": "./dist/commands/index.d.ts",
      "import": "./dist/commands/index.mjs",
      "require": "./dist/commands/index.js"
    },
    "./events": {
      "types": "./dist/events/index.d.ts",
      "import": "./dist/events/index.mjs",
      "require": "./dist/events/index.js"
    },
    "./queries": {
      "types": "./dist/queries/index.d.ts",
      "import": "./dist/queries/index.mjs",
      "require": "./dist/queries/index.js"
    }
  },
  "scripts": {
    "build": "tsup",
    "dev": "tsup --watch",
    "test": "vitest",
    "clean": "rimraf dist"
  },
  "dependencies": {
    "neverthrow": "^6.1.0",
    "uuid": "^9.0.0",
    "zod": "^3.22.0"
  },
  "devDependencies": {
    "@types/uuid": "^9.0.0",
    "tsup": "^8.0.0",
    "vitest": "^1.0.0"
  },
  "files": [
    "dist",
    "src"
  ],
  "sideEffects": false,
  "publishConfig": {
    "access": "public"
  }
}
```

#### packages/core/tsconfig.json
```json
{
  "extends": "../../../tsconfig.base.json",
  "compilerOptions": {
    "outDir": "./dist",
    "rootDir": "./",
    "baseUrl": ".",
    "paths": {
      "@/*": ["./src/*"]
    }
  },
  "include": ["src/**/*", "index.ts"],
  "exclude": ["dist", "node_modules", "**/*.test.ts"]
}
```

#### packages/core/tsup.config.ts
```typescript
import { defineConfig } from 'tsup'

export default defineConfig({
  entry: {
    index: 'index.ts',
    'aggregates/index': 'aggregates/index.ts',
    'commands/index': 'commands/index.ts',
    'events/index': 'events/index.ts',
    'queries/index': 'queries/index.ts',
  },
  format: ['cjs', 'esm'],
  dts: true,
  sourcemap: true,
  clean: true,
  treeshake: true,
})
```

## 開発ガイドライン

### 命名規則

1. **ファイル名**
   - ケバブケース: `event-store.ts`
   - テストファイル: `event-store.test.ts`
   - 型定義: `types.ts`

2. **エクスポート**
   - 各ディレクトリに`index.ts`を配置
   - 明示的な再エクスポート
   - バレルエクスポートは最小限に

3. **インポート**
   - 相対パスは同一パッケージ内のみ
   - 他パッケージは`@sekiban/*`を使用

### 依存関係管理

1. **パッケージ間の依存**
   ```
   @sekiban/dapr → @sekiban/core
   @sekiban/cosmos → @sekiban/core
   @sekiban/postgres → @sekiban/core
   @sekiban/testing → @sekiban/core
   ```

2. **循環依存の防止**
   - coreは他のパッケージに依存しない
   - ストレージプロバイダーは相互に依存しない

### テスト戦略

1. **ユニットテスト**
   - 各パッケージ内で完結
   - モックを活用した単体テスト

2. **統合テスト**
   - パッケージ間の連携確認
   - 実際のストレージを使用

3. **E2Eテスト**
   - 実際のユースケースをシミュレート
   - CI/CD環境での自動実行

## ビルドとリリース

### ビルドプロセス

1. **開発ビルド**
   ```bash
   pnpm build        # 全パッケージビルド
   pnpm -F @sekiban/core build  # 特定パッケージ
   ```

2. **ウォッチモード**
   ```bash
   pnpm dev          # 変更監視
   ```

### リリースプロセス

1. **変更記録**
   ```bash
   pnpm changeset
   ```

2. **バージョン更新**
   ```bash
   pnpm version
   ```

3. **パブリッシュ**
   ```bash
   pnpm publish
   ```

## CI/CD

### GitHub Actions

1. **CI Pipeline**
   - プルリクエスト時の自動テスト
   - 型チェックとリント
   - ビルド確認

2. **リリース Pipeline**
   - タグプッシュ時の自動公開
   - npm レジストリへの公開
   - ドキュメント更新

## まとめ

この構成により、以下を実現します：

1. **モジュラー設計** - 機能ごとに分離されたパッケージ
2. **段階的な採用** - 必要な機能だけを選択して使用可能
3. **型安全性** - TypeScriptの型システムを最大限活用
4. **テスタビリティ** - 各レベルでのテストが容易
5. **メンテナンス性** - 明確な責務分離と依存関係

開発者はこの構造に従って、効率的にSekiban TypeScript版の開発を進めることができます。