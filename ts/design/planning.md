# TypeScript版Sekiban.Pure.Dapr設計ドキュメント

## 概要

このドキュメントでは、Sekiban.Pure.DaprのTypeScript実装について、アーキテクチャ設計と実装方針を定義します。

## 背景と目的

### 現状の課題
- Sekiban.Pure.DaprはC#でのみ実装されている
- TypeScriptでDaprアクターを使用したい場合、別途実装が必要
- C#とTypeScriptでイベント定義を二重管理する必要がある

### 目的
- TypeScriptでSekibanのイベントソーシング機能を利用可能にする
- C#実装との相互運用性を確保する
- 段階的な移行パスを提供する

## アーキテクチャ概要

### 実装アプローチ：ハイブリッド型

初期フェーズではハイブリッドアプローチを採用し、以下の構成とします：

1. **コアアクター（C#）**
   - `AggregateEventHandlerActor`: イベントの永続化と読み込みを担当
   - 既存のC#実装をそのまま利用

2. **クライアントライブラリ（TypeScript）**
   - Daprアクタープロキシを使用してC#アクターと通信
   - イベントとコマンドのシリアライゼーション/デシリアライゼーション
   - TypeScript向けの型定義とインターフェース

3. **将来的な拡張（TypeScript）**
   - 必要に応じて`AggregateActor`をTypeScriptで実装
   - 完全なTypeScript実装への移行パス

## TypeScript/C#相互運用戦略

### シリアライゼーション仕様

#### 1. JSON形式の統一
```typescript
// TypeScript側の設定
const jsonOptions = {
  // C#のSystem.Text.Json互換設定
  propertyNamingPolicy: 'camelCase',
  includeFields: false,
  ignoreNullValues: false,
  writeIndented: false
};
```

#### 2. イベントドキュメント構造
```typescript
interface SerializableEventDocument {
  // EventDocumentのフラットなプロパティ
  id: string;                    // Guid
  sortableUniqueId: string;
  version: number;
  
  // PartitionKeysのフラットなプロパティ  
  aggregateId: string;           // Guid
  aggregateGroup: string;
  rootPartitionKey: string;
  
  // イベント情報
  payloadTypeName: string;
  timeStamp: string;             // ISO 8601形式
  partitionKey: string;
  
  // EventMetadataのフラットなプロパティ
  causationId: string;
  correlationId: string;
  executedUser: string;
  
  // IEventPayloadを圧縮したデータ
  compressedPayloadJson: string; // Base64エンコード
  
  // アプリケーションバージョン情報
  payloadAssemblyVersion: string;
}
```

#### 3. 型名マッピング
C#とTypeScript間で型名を一致させる必要があります：

```typescript
// TypeScript
@EventType('UserCreated')
class UserCreated implements IEventPayload {
  constructor(public userId: string, public name: string) {}
}

// 対応するC#
[GenerateSerializer]
public record UserCreated(string UserId, string Name) : IEventPayload;
```

### 通信プロトコル

#### 1. アクター呼び出し
```typescript
// TypeScriptからC#アクターを呼び出す
const actorProxy = new ActorProxy(
  actorId,
  'AggregateEventHandlerActor',
  daprClient
);

const response = await actorProxy.invoke<SerializableEventDocument[]>(
  'GetAllEventsAsync'
);
```

#### 2. データ圧縮
- C#側と同じGZip圧縮アルゴリズムを使用
- Node.jsの`zlib`モジュールを利用

```typescript
import { gzip, gunzip } from 'zlib';
import { promisify } from 'util';

const compress = promisify(gzip);
const decompress = promisify(gunzip);
```

## 実装ロードマップ

### フェーズ1：基本クライアントライブラリ（1-2週間）
1. プロジェクト構造の設定
2. 基本的な型定義
3. Daprアクタープロキシのラッパー
4. シリアライゼーション/デシリアライゼーション実装
5. 基本的なテストケース

### フェーズ2：イベントソーシング機能（2-3週間）
1. イベントストリームの読み込み
2. コマンド実行のサポート
3. アグリゲート状態の取得
4. エラーハンドリング
5. 統合テスト

### フェーズ3：高度な機能（3-4週間）
1. マルチプロジェクション対応
2. クエリハンドラー実装
3. パフォーマンス最適化
4. ドキュメント作成

### フェーズ4：TypeScriptアクター実装（オプション、4-6週間）
1. AggregateActorのTypeScript実装
2. 状態管理の実装
3. C#実装との互換性テスト
4. 移行ガイドの作成

## ディレクトリ構造

```
ts/
├── design/
│   └── planning.md              # このドキュメント
├── src/
│   ├── actors/
│   │   ├── actor-proxy.ts      # Daprアクタープロキシのラッパー
│   │   ├── aggregate-actor.ts  # 将来的なTypeScript実装
│   │   └── types.ts            # アクターインターフェース定義
│   ├── serialization/
│   │   ├── event-document.ts   # SerializableEventDocument実装
│   │   ├── compression.ts      # GZip圧縮/解凍ユーティリティ
│   │   └── json-options.ts     # JSON設定
│   ├── events/
│   │   ├── event-types.ts      # イベント型定義
│   │   ├── event-registry.ts   # イベント型レジストリ
│   │   └── decorators.ts       # TypeScriptデコレーター
│   ├── client/
│   │   ├── sekiban-dapr-client.ts  # メインクライアントクラス
│   │   └── command-executor.ts     # コマンド実行ロジック
│   └── index.ts                     # パッケージエントリポイント
├── tests/
│   ├── unit/                    # ユニットテスト
│   └── integration/             # 統合テスト
├── examples/                    # 使用例
├── package.json
├── tsconfig.json
└── README.md
```

## 技術選定

### 必須依存関係
- `@dapr/dapr`: Dapr JavaScript SDK
- `zlib`: 圧縮/解凍（Node.js標準）
- `uuid`: GUID生成

### 開発依存関係
- `typescript`: TypeScriptコンパイラ
- `@types/node`: Node.js型定義
- `jest`: テストフレームワーク
- `ts-jest`: TypeScript用Jestプリセット
- `eslint`: リンター
- `prettier`: コードフォーマッター

## 設定例

### package.json
```json
{
  "name": "@sekiban/pure-dapr",
  "version": "0.1.0",
  "description": "TypeScript client for Sekiban.Pure.Dapr",
  "main": "dist/index.js",
  "types": "dist/index.d.ts",
  "scripts": {
    "build": "tsc",
    "test": "jest",
    "lint": "eslint src/**/*.ts",
    "format": "prettier --write src/**/*.ts"
  },
  "dependencies": {
    "@dapr/dapr": "^3.0.0",
    "uuid": "^9.0.0"
  },
  "devDependencies": {
    "@types/node": "^20.0.0",
    "@types/uuid": "^9.0.0",
    "typescript": "^5.0.0",
    "jest": "^29.0.0",
    "ts-jest": "^29.0.0",
    "@typescript-eslint/parser": "^6.0.0",
    "@typescript-eslint/eslint-plugin": "^6.0.0",
    "prettier": "^3.0.0"
  }
}
```

### tsconfig.json
```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "commonjs",
    "lib": ["ES2022"],
    "outDir": "./dist",
    "rootDir": "./src",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "forceConsistentCasingInFileNames": true,
    "declaration": true,
    "declarationMap": true,
    "sourceMap": true,
    "experimentalDecorators": true,
    "emitDecoratorMetadata": true
  },
  "include": ["src/**/*"],
  "exclude": ["node_modules", "dist", "tests"]
}
```

## 考慮事項

### パフォーマンス
- GZip圧縮/解凍のオーバーヘッド
- ネットワーク通信のレイテンシ
- 大量イベントの処理

### セキュリティ
- アクター間通信の認証
- データの暗号化（必要に応じて）
- 入力検証

### 互換性
- C#とTypeScript間のバージョニング
- スキーマ進化への対応
- 後方互換性の維持

### テスト戦略
- ユニットテスト：個々のコンポーネント
- 統合テスト：C#アクターとの通信
- E2Eテスト：実際のDapr環境での動作確認

## まとめ

このハイブリッドアプローチにより、既存のC#実装を活用しながら、TypeScriptでSekibanを使用できるようになります。将来的には完全なTypeScript実装への移行も可能ですが、まずは実用的なクライアントライブラリの提供に焦点を当てます。