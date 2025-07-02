# TypeScript版Sekiban.Pure.Dapr実装レビュー（2024-2025年最新トレンド）

## エグゼクティブサマリー

TypeScript版Sekiban.Pure.Daprの設計において、2024-2025年の最新技術トレンドを踏まえた実装指針を以下にまとめます。特に、TypeScript 5.6の新機能、Dapr SDK 3.5.xの進化、そして高速ビルドツールの活用により、C#実装との相互運用性を保ちながら、よりモダンで高性能な実装が可能になっています。

## 1. TypeScript 5.6+の積極活用機能

### 1.1 標準ECMAScriptデコレーター（Stage 3）

```typescript
// イベント型定義のデコレーター実装例
function EventType(typeName: string) {
  return function<T extends { new(...args: any[]): {} }>(constructor: T) {
    return class extends constructor {
      static readonly eventTypeName = typeName;
      readonly $type = typeName; // C#互換のための型情報
    };
  };
}

@EventType('UserCreated')
export class UserCreated implements IEventPayload {
  constructor(
    public readonly userId: string,
    public readonly name: string,
    public readonly createdAt: Date
  ) {}
}
```

### 1.2 const type parametersの活用

```typescript
// PartitionKeysの型安全な実装
export class PartitionKeys<TProjector extends string> {
  static generate<const T extends string>(
    projectorName: T,
    rootPartitionKey = 'default'
  ): PartitionKeys<T> {
    return new PartitionKeys(
      crypto.randomUUID(),
      projectorName,
      rootPartitionKey
    );
  }

  static existing<const T extends string>(
    projectorName: T,
    aggregateId: string,
    rootPartitionKey = 'default'
  ): PartitionKeys<T> {
    return new PartitionKeys(aggregateId, projectorName, rootPartitionKey);
  }
}
```

### 1.3 using宣言によるリソース管理

```typescript
// 圧縮ストリームの安全な管理
async function compressPayload(data: unknown): Promise<string> {
  using stream = createGzipStream();
  const jsonData = JSON.stringify(data);
  const compressed = await stream.compress(jsonData);
  return Buffer.from(compressed).toString('base64');
} // streamは自動的にクリーンアップされる
```

## 2. Dapr JavaScript/TypeScript SDK 3.5.xの最新機能活用

### 2.1 Namespaced Actorsによるマルチテナント対応

```typescript
export class SekibanDaprClient {
  private readonly actorProxyFactory: ActorProxyFactory;

  constructor(
    private readonly daprClient: DaprClient,
    private readonly namespace?: string
  ) {
    this.actorProxyFactory = new ActorProxyFactory(daprClient);
  }

  async getAggregateEventHandlerActor<T extends IAggregatePayload>(
    partitionKeys: PartitionKeys<string>
  ): Promise<ActorProxy> {
    const actorId = this.namespace 
      ? `${this.namespace}:${partitionKeys.aggregateId}`
      : partitionKeys.aggregateId;
    
    return this.actorProxyFactory.createActorProxy(
      ActorId.of(actorId),
      'AggregateEventHandlerActor'
    );
  }
}
```

### 2.2 Workflow APIのSuspend/Resume活用

```typescript
// 長時間実行のサガ実装
export class OrderSagaWorkflow extends Workflow {
  async runWorkflow(ctx: WorkflowContext, order: Order): Promise<void> {
    // 在庫確認
    const inventory = await ctx.callActivity('checkInventory', order);
    
    if (!inventory.available) {
      // 在庫待ちで一時停止
      await ctx.suspend('waiting-for-inventory');
      // resumeされたら再開
    }
    
    // 決済処理
    const payment = await ctx.callActivity('processPayment', order);
    // ...
  }
}
```

## 3. C#/TypeScript相互運用の具体的実装

### 3.1 統一シリアライゼーション設定

```typescript
// C# System.Text.Json互換のJSON設定
export const jsonSerializerOptions = {
  // プロパティ名の変換
  propertyNamingPolicy: 'camelCase',
  
  // DateTimeのISO 8601フォーマット
  dateHandler: {
    serialize: (date: Date) => date.toISOString(),
    deserialize: (value: string) => new Date(value)
  },
  
  // GUIDの小文字ハイフン区切り
  guidHandler: {
    serialize: (guid: string) => guid.toLowerCase(),
    deserialize: (value: string) => value.toLowerCase()
  }
};
```

### 3.2 SerializableEventDocument実装

```typescript
import { z } from 'zod';

// Zodスキーマによる型安全なシリアライゼーション
export const SerializableEventDocumentSchema = z.object({
  // EventDocumentのフラットなプロパティ
  id: z.string().uuid(),
  sortableUniqueId: z.string(),
  version: z.number().int().positive(),
  
  // PartitionKeysのフラットなプロパティ
  aggregateId: z.string().uuid(),
  aggregateGroup: z.string(),
  rootPartitionKey: z.string(),
  
  // イベント情報
  payloadTypeName: z.string(),
  timeStamp: z.string().datetime({ offset: true }),
  partitionKey: z.string(),
  
  // EventMetadataのフラットなプロパティ
  causationId: z.string(),
  correlationId: z.string(),
  executedUser: z.string(),
  
  // 圧縮されたペイロード
  compressedPayloadJson: z.string(),
  
  // バージョン情報
  payloadAssemblyVersion: z.string()
});

export type SerializableEventDocument = z.infer<typeof SerializableEventDocumentSchema>;

// 型安全なシリアライゼーション/デシリアライゼーション
export class EventDocumentSerializer {
  static async serialize(event: IEventDocument): Promise<SerializableEventDocument> {
    const compressedPayload = await compressPayload(event.payload);
    
    return SerializableEventDocumentSchema.parse({
      id: event.id,
      sortableUniqueId: event.sortableUniqueId,
      version: event.version,
      aggregateId: event.partitionKeys.aggregateId,
      aggregateGroup: event.partitionKeys.aggregateGroup,
      rootPartitionKey: event.partitionKeys.rootPartitionKey,
      payloadTypeName: event.payload.constructor.name,
      timeStamp: event.timeStamp.toISOString(),
      partitionKey: event.partitionKey,
      causationId: event.metadata.causationId,
      correlationId: event.metadata.correlationId,
      executedUser: event.metadata.executedUser,
      compressedPayloadJson: compressedPayload,
      payloadAssemblyVersion: '1.0.0'
    });
  }
  
  static async deserialize(doc: SerializableEventDocument): Promise<IEventDocument> {
    // 検証
    const validated = SerializableEventDocumentSchema.parse(doc);
    
    // ペイロードの解凍とデシリアライゼーション
    const payload = await decompressPayload(
      validated.compressedPayloadJson,
      validated.payloadTypeName
    );
    
    return {
      id: validated.id,
      sortableUniqueId: validated.sortableUniqueId,
      version: validated.version,
      partitionKeys: new PartitionKeys(
        validated.aggregateId,
        validated.aggregateGroup,
        validated.rootPartitionKey
      ),
      payload,
      timeStamp: new Date(validated.timeStamp),
      partitionKey: validated.partitionKey,
      metadata: {
        causationId: validated.causationId,
        correlationId: validated.correlationId,
        executedUser: validated.executedUser
      }
    };
  }
}
```

## 4. 高性能イベントソーシング実装パターン

### 4.1 機能的アグリゲート実装

```typescript
// Pure Functionとしてのアグリゲート
export abstract class AggregateProjector<TPayload extends IAggregatePayload> {
  // イミュータブルな状態遷移
  static evolve<T extends IAggregatePayload>(
    events: IEventPayload[],
    initialState?: T
  ): T {
    return events.reduce((state, event) => {
      const handler = this.getEventHandler(event.constructor.name);
      return handler ? handler(state, event) : state;
    }, initialState || this.getInitialState());
  }
  
  // 型安全なイベントハンドラー登録
  private static eventHandlers = new Map<string, EventHandler<any, any>>();
  
  static on<TEvent extends IEventPayload>(
    eventType: new (...args: any[]) => TEvent,
    handler: (state: TPayload, event: TEvent) => TPayload
  ): void {
    this.eventHandlers.set(eventType.name, handler);
  }
}

// 使用例
export class UserProjector extends AggregateProjector<User> {
  static {
    this.on(UserCreated, (state, event) => ({
      ...state,
      id: event.userId,
      name: event.name,
      status: 'active'
    }));
    
    this.on(UserUpdated, (state, event) => ({
      ...state,
      name: event.name,
      updatedAt: event.occurredAt
    }));
  }
}
```

### 4.2 非同期イテレーターによるイベントストリーム

```typescript
export class EventStream {
  async *readEvents(
    partitionKeys: PartitionKeys<string>,
    fromVersion = 0
  ): AsyncIterableIterator<IEventDocument> {
    const actor = await this.client.getAggregateEventHandlerActor(partitionKeys);
    
    let currentVersion = fromVersion;
    const batchSize = 100;
    
    while (true) {
      const events = await actor.invoke<SerializableEventDocument[]>(
        'GetEventsAsync',
        { fromVersion: currentVersion, limit: batchSize }
      );
      
      if (events.length === 0) break;
      
      for (const event of events) {
        yield await EventDocumentSerializer.deserialize(event);
        currentVersion = event.version;
      }
      
      if (events.length < batchSize) break;
    }
  }
}
```

## 5. モノレポ構成とビルド最適化

### 5.1 推奨ディレクトリ構造（更新版）

```
sekiban/
├── .github/
│   └── workflows/
│       └── ts-ci.yml               # TypeScript専用CI
├── packages/                       # Rush.jsで管理
│   ├── @sekiban/core/              # コアライブラリ
│   ├── @sekiban/dapr-client/       # Daprクライアント
│   ├── @sekiban/testing/           # テストユーティリティ
│   └── @sekiban/examples/          # サンプル実装
├── apps/                           # Turborepoで管理
│   ├── web-ui/                     # Vite + React管理画面
│   └── cli/                        # CLIツール
├── rush.json                       # Rush設定
├── turbo.json                      # Turborepo設定
└── tsconfig.base.json              # 共通TypeScript設定
```

### 5.2 高速ビルド設定（esbuild）

```javascript
// build.mjs - esbuildによる高速ビルド
import { build } from 'esbuild';
import { nodeExternalsPlugin } from 'esbuild-node-externals';

await build({
  entryPoints: ['src/index.ts'],
  bundle: true,
  platform: 'node',
  target: 'node20',
  format: 'esm',
  outfile: 'dist/index.js',
  
  // TypeScript 5.6の新機能を活用
  tsconfig: './tsconfig.json',
  
  // デコレーターメタデータの保持
  keepNames: true,
  
  // 外部依存の除外
  plugins: [nodeExternalsPlugin()],
  
  // 最適化設定
  minify: true,
  treeShaking: true,
  
  // ソースマップ
  sourcemap: 'linked',
  
  // メタファイル出力（バンドル分析用）
  metafile: true,
  
  // Watch mode for development
  watch: process.env.NODE_ENV === 'development'
});
```

### 5.3 Viteによる開発環境最適化

```typescript
// vite.config.ts - 管理画面用
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { resolve } from 'path';

export default defineConfig({
  plugins: [react()],
  
  resolve: {
    alias: {
      '@sekiban/core': resolve(__dirname, '../../packages/core/src'),
      '@sekiban/dapr-client': resolve(__dirname, '../../packages/dapr-client/src')
    }
  },
  
  // esbuildによる高速トランスパイル
  esbuild: {
    target: 'es2022',
    
    // TypeScript enumsの最適化
    keepNames: true
  },
  
  // 最適化設定
  build: {
    target: 'es2022',
    
    // Rollupオプション
    rollupOptions: {
      output: {
        manualChunks: {
          'vendor': ['react', 'react-dom'],
          'sekiban': ['@sekiban/core', '@sekiban/dapr-client']
        }
      }
    }
  },
  
  // 開発サーバー設定
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:3500', // Dapr sidecar
        changeOrigin: true
      }
    }
  }
});
```

## 6. テスト戦略の具体的実装

### 6.1 Vitestによる高速ユニットテスト

```typescript
// vitest.config.ts
import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    globals: true,
    environment: 'node',
    
    // TypeScript設定
    typecheck: {
      tsconfig: './tsconfig.test.json'
    },
    
    // カバレッジ設定
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      exclude: ['**/node_modules/**', '**/dist/**']
    },
    
    // セットアップファイル
    setupFiles: ['./tests/setup.ts'],
    
    // 並列実行
    pool: 'threads',
    poolOptions: {
      threads: {
        singleThread: false
      }
    }
  }
});
```

### 6.2 Playwrightによる統合テスト

```typescript
// tests/integration/dapr-actor.spec.ts
import { test, expect } from '@playwright/test';
import { DaprServer } from '@dapr/dapr';
import { SekibanDaprClient } from '@sekiban/dapr-client';

test.describe('Dapr Actor Integration', () => {
  let daprServer: DaprServer;
  let client: SekibanDaprClient;
  
  test.beforeAll(async () => {
    // Daprサイドカーの起動
    daprServer = new DaprServer({
      appId: 'sekiban-test',
      appPort: 3000,
      daprPort: 3500
    });
    
    await daprServer.start();
    client = new SekibanDaprClient(daprServer.client);
  });
  
  test.afterAll(async () => {
    await daprServer.stop();
  });
  
  test('should save and retrieve events', async () => {
    const partitionKeys = PartitionKeys.generate<'UserProjector'>('UserProjector');
    const event = new UserCreated('123', 'Test User', new Date());
    
    // イベントの保存
    await client.saveEvent(partitionKeys, event);
    
    // イベントの取得
    const events = await client.getEvents(partitionKeys);
    expect(events).toHaveLength(1);
    expect(events[0].payload).toMatchObject({
      userId: '123',
      name: 'Test User'
    });
  });
});
```

## 7. パフォーマンス監視と最適化

### 7.1 OpenTelemetryによる分散トレーシング

```typescript
import { NodeSDK } from '@opentelemetry/sdk-node';
import { getNodeAutoInstrumentations } from '@opentelemetry/auto-instrumentations-node';
import { Resource } from '@opentelemetry/resources';
import { SemanticResourceAttributes } from '@opentelemetry/semantic-conventions';

// OpenTelemetry設定
const sdk = new NodeSDK({
  resource: new Resource({
    [SemanticResourceAttributes.SERVICE_NAME]: 'sekiban-ts',
    [SemanticResourceAttributes.SERVICE_VERSION]: '1.0.0'
  }),
  instrumentations: [
    getNodeAutoInstrumentations({
      '@opentelemetry/instrumentation-fs': {
        enabled: false // ファイルシステムの計測は無効化
      }
    })
  ]
});

// カスタムスパンの作成
export function traceEventProcessing() {
  return function (
    target: any,
    propertyKey: string,
    descriptor: PropertyDescriptor
  ) {
    const originalMethod = descriptor.value;
    
    descriptor.value = async function (...args: any[]) {
      const span = tracer.startSpan(`event.${propertyKey}`, {
        attributes: {
          'event.type': args[0]?.constructor?.name,
          'aggregate.id': args[1]?.aggregateId
        }
      });
      
      try {
        const result = await originalMethod.apply(this, args);
        span.setStatus({ code: SpanStatusCode.OK });
        return result;
      } catch (error) {
        span.recordException(error as Error);
        span.setStatus({ 
          code: SpanStatusCode.ERROR,
          message: error.message
        });
        throw error;
      } finally {
        span.end();
      }
    };
  };
}
```

### 7.2 メモリ使用量の最適化

```typescript
// イベントストリームのメモリ効率的な処理
export class OptimizedEventProcessor {
  private readonly batchSize = 1000;
  private readonly maxConcurrency = 10;
  
  async processLargeEventStream(
    partitionKeys: PartitionKeys<string>
  ): Promise<void> {
    const eventStream = this.eventStore.readEvents(partitionKeys);
    
    // バッチ処理とバックプレッシャー
    const batches: IEventDocument[][] = [];
    let currentBatch: IEventDocument[] = [];
    
    for await (const event of eventStream) {
      currentBatch.push(event);
      
      if (currentBatch.length >= this.batchSize) {
        batches.push(currentBatch);
        currentBatch = [];
        
        // 並列処理数の制限
        if (batches.length >= this.maxConcurrency) {
          await Promise.all(
            batches.map(batch => this.processBatch(batch))
          );
          batches.length = 0;
        }
      }
    }
    
    // 残りの処理
    if (currentBatch.length > 0) {
      batches.push(currentBatch);
    }
    
    await Promise.all(
      batches.map(batch => this.processBatch(batch))
    );
  }
}
```

## 8. 実装推奨事項とベストプラクティス

### 8.1 エラーハンドリング戦略

```typescript
// ResultBox相当の実装
export type Result<T, E = Error> = 
  | { ok: true; value: T }
  | { ok: false; error: E };

export class ResultBox<T> {
  private constructor(private readonly result: Result<T>) {}
  
  static ok<T>(value: T): ResultBox<T> {
    return new ResultBox({ ok: true, value });
  }
  
  static error<T>(error: Error): ResultBox<T> {
    return new ResultBox<T>({ ok: false, error });
  }
  
  // モナディックな操作
  map<U>(fn: (value: T) => U): ResultBox<U> {
    if (this.result.ok) {
      return ResultBox.ok(fn(this.result.value));
    }
    return ResultBox.error(this.result.error);
  }
  
  flatMap<U>(fn: (value: T) => ResultBox<U>): ResultBox<U> {
    if (this.result.ok) {
      return fn(this.result.value);
    }
    return ResultBox.error(this.result.error);
  }
  
  // Railwayパターン
  conveyor<U>(fn: (value: T) => ResultBox<U>): ResultBox<U> {
    return this.flatMap(fn);
  }
  
  unwrapOr(defaultValue: T): T {
    return this.result.ok ? this.result.value : defaultValue;
  }
  
  unwrapBox(): T {
    if (!this.result.ok) {
      throw this.result.error;
    }
    return this.result.value;
  }
}
```

### 8.2 開発体験の向上

```typescript
// CLIツールの実装例
import { Command } from 'commander';
import { SekibanDaprClient } from '@sekiban/dapr-client';

const program = new Command();

program
  .name('sekiban')
  .description('Sekiban CLI for TypeScript')
  .version('1.0.0');

program
  .command('replay')
  .description('Replay events for an aggregate')
  .argument('<aggregateId>', 'Aggregate ID')
  .option('-g, --group <group>', 'Aggregate group', 'default')
  .action(async (aggregateId, options) => {
    const client = new SekibanDaprClient();
    const partitionKeys = PartitionKeys.existing(
      options.group,
      aggregateId
    );
    
    console.log('Replaying events...');
    
    const eventStream = client.getEventStream(partitionKeys);
    let count = 0;
    
    for await (const event of eventStream) {
      console.log(`[${++count}] ${event.payload.constructor.name}`, {
        version: event.version,
        timestamp: event.timeStamp
      });
    }
    
    console.log(`Replayed ${count} events`);
  });

program.parse();
```

## まとめ

TypeScript版Sekiban.Pure.Daprの実装において、以下の点が重要です：

1. **TypeScript 5.6+の新機能**を積極的に活用し、型安全性とDXを向上
2. **Dapr SDK 3.5.x**の新機能により、C#実装との共存が容易に
3. **Zod + JSON Schema**による型契約の共有で、相互運用性を確保
4. **esbuild/Vite**による高速ビルドで、開発効率を最大化
5. **Vitest + Playwright**で高速かつ信頼性の高いテストを実現
6. **OpenTelemetry**による本番環境での監視と最適化

これらの実装により、C#版と同等以上の性能と開発体験を実現できます。