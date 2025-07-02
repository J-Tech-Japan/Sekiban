# TypeScript版Sekiban.Pure.Dapr設計レビュー

## エグゼクティブサマリー

planning.mdで提案されたハイブリッドアプローチは、実装リスクを最小化しながら段階的な移行を可能にする優れた戦略です。以下、詳細な技術的検討事項と推奨事項をまとめます。

## 1. アーキテクチャ設計の評価

### 1.1 ハイブリッドアプローチの利点
- **リスク軽減**: 既存のC#実装を活用することで、初期実装のリスクを大幅に削減
- **段階的移行**: 将来的な完全TypeScript化への明確なパスを提供
- **早期価値提供**: 基本機能を1-2週間で提供可能

### 1.2 潜在的な課題
- **パフォーマンスオーバーヘッド**: アクター間通信による追加レイテンシ
- **デバッグの複雑性**: 言語境界をまたぐデバッグの困難さ
- **バージョン管理**: C#とTypeScriptのスキーマ同期の必要性

## 2. C#/TypeScript相互運用の技術的考察

### 2.1 シリアライゼーション戦略

#### 重要な考慮事項
1. **日付フォーマット**
   ```typescript
   // ISO 8601形式の厳密な実装
   class DateTimeSerializer {
     static serialize(date: Date): string {
       // C#のDateTime.UtcNowと完全互換
       return date.toISOString(); // "2024-01-15T10:30:00.000Z"
     }
     
     static deserialize(iso: string): Date {
       const date = new Date(iso);
       if (isNaN(date.getTime())) {
         throw new Error(`Invalid ISO date: ${iso}`);
       }
       return date;
     }
   }
   ```

2. **GUID処理**
   ```typescript
   // C# Guidとの互換性確保
   import { v4 as uuidv4, validate as uuidValidate } from 'uuid';
   
   class GuidHandler {
     static generate(): string {
       // C#形式: "550e8400-e29b-41d4-a716-446655440000"
       return uuidv4().toLowerCase();
     }
     
     static validate(guid: string): boolean {
       return uuidValidate(guid) && guid === guid.toLowerCase();
     }
     
     static normalize(guid: string): string {
       // C#のGuid.ToString()と同じ形式に正規化
       return guid.toLowerCase();
     }
   }
   ```

3. **数値精度**
   ```typescript
   // JavaScriptの数値精度制限への対処
   interface NumericConstraints {
     // JavaScript安全整数範囲: -(2^53-1) to 2^53-1
     readonly MAX_SAFE_INTEGER: number; // 9007199254740991
     readonly MIN_SAFE_INTEGER: number; // -9007199254740991
   }
   
   class NumericValidator {
     static validateInteger(value: number): void {
       if (!Number.isSafeInteger(value)) {
         throw new Error(`Unsafe integer: ${value}`);
       }
     }
     
     static validateDecimal(value: number, precision: number): void {
       const multiplier = Math.pow(10, precision);
       const rounded = Math.round(value * multiplier) / multiplier;
       if (rounded !== value) {
         console.warn(`Precision loss detected: ${value} -> ${rounded}`);
       }
     }
   }
   ```

### 2.2 型システムの整合性

#### 推奨される型マッピング戦略
```typescript
// 型マッピング定義
type TypeMapping = {
  // C# -> TypeScript
  'System.String': string;
  'System.Int32': number;
  'System.Int64': number | bigint; // 精度に注意
  'System.Decimal': string; // 文字列として扱う
  'System.DateTime': string; // ISO 8601
  'System.Guid': string;
  'System.Boolean': boolean;
};

// 実行時型検証
class TypeValidator {
  static validateEventPayload(payload: unknown, schema: Schema): void {
    // JSON Schemaベースの検証
    const ajv = new Ajv();
    const validate = ajv.compile(schema);
    if (!validate(payload)) {
      throw new ValidationError(validate.errors);
    }
  }
}
```

### 2.3 圧縮の互換性

```typescript
import { gzip, gunzip } from 'zlib';
import { promisify } from 'util';

class CompressionService {
  private static readonly COMPRESSION_LEVEL = 6; // C#のデフォルトと同じ
  
  static async compress(data: string): Promise<string> {
    const buffer = Buffer.from(data, 'utf-8');
    const compressed = await promisify(gzip)(buffer, {
      level: this.COMPRESSION_LEVEL
    });
    return compressed.toString('base64');
  }
  
  static async decompress(data: string): Promise<string> {
    const buffer = Buffer.from(data, 'base64');
    const decompressed = await promisify(gunzip)(buffer);
    return decompressed.toString('utf-8');
  }
}
```

## 3. セキュリティ考慮事項

### 3.1 認証・認可
```typescript
interface SecurityConfiguration {
  // Dapr API token
  daprApiToken?: string;
  
  // mTLS設定
  mtls: {
    enabled: boolean;
    clientCert?: string;
    clientKey?: string;
    serverCA?: string;
  };
  
  // データ暗号化
  encryption: {
    enabled: boolean;
    algorithm: 'AES-256-GCM';
    keyRotationInterval: number; // days
  };
}
```

### 3.2 入力検証
```typescript
class SecurityValidator {
  // SQLインジェクション対策
  static sanitizeString(input: string): string {
    return input.replace(/[^\w\s-_.]/gi, '');
  }
  
  // XXE攻撃対策
  static validateXml(xml: string): boolean {
    // XMLパーサーでDTD無効化
    return true;
  }
  
  // JSONインジェクション対策
  static validateJson(json: string): unknown {
    try {
      return JSON.parse(json);
    } catch (e) {
      throw new ValidationError('Invalid JSON');
    }
  }
}
```

## 4. パフォーマンス最適化戦略

### 4.1 バッチ処理
```typescript
class EventBatchProcessor {
  private readonly batchSize = 100;
  private readonly maxLatency = 50; // ms
  
  async processBatch(events: Event[]): Promise<void> {
    const batches = this.createBatches(events);
    await Promise.all(
      batches.map(batch => this.processSingleBatch(batch))
    );
  }
  
  private createBatches(events: Event[]): Event[][] {
    const batches: Event[][] = [];
    for (let i = 0; i < events.length; i += this.batchSize) {
      batches.push(events.slice(i, i + this.batchSize));
    }
    return batches;
  }
}
```

### 4.2 キャッシング戦略
```typescript
interface CacheStrategy {
  // イベントストリームキャッシュ
  eventCache: {
    maxSize: number;
    ttl: number; // seconds
    strategy: 'LRU' | 'LFU';
  };
  
  // アグリゲート状態キャッシュ
  stateCache: {
    enabled: boolean;
    invalidationStrategy: 'event-based' | 'time-based';
  };
}
```

## 5. エラーハンドリングとリトライ戦略

```typescript
class RetryPolicy {
  private readonly maxRetries = 3;
  private readonly baseDelay = 1000; // ms
  
  async executeWithRetry<T>(
    operation: () => Promise<T>,
    context: string
  ): Promise<T> {
    let lastError: Error | undefined;
    
    for (let attempt = 0; attempt <= this.maxRetries; attempt++) {
      try {
        return await operation();
      } catch (error) {
        lastError = error as Error;
        
        if (!this.isRetryable(error) || attempt === this.maxRetries) {
          throw new RetryError(context, attempt, lastError);
        }
        
        const delay = this.calculateDelay(attempt);
        await this.sleep(delay);
      }
    }
    
    throw lastError!;
  }
  
  private isRetryable(error: unknown): boolean {
    // ネットワークエラーやタイムアウトのみリトライ
    return error instanceof NetworkError || 
           error instanceof TimeoutError;
  }
  
  private calculateDelay(attempt: number): number {
    // Exponential backoff with jitter
    const exponentialDelay = this.baseDelay * Math.pow(2, attempt);
    const jitter = Math.random() * 0.3 * exponentialDelay;
    return exponentialDelay + jitter;
  }
}
```

## 6. テスト戦略の詳細

### 6.1 相互運用性テスト
```typescript
describe('C# Interoperability Tests', () => {
  test('Event serialization compatibility', async () => {
    // C#で生成されたイベントJSONをデシリアライズ
    const csharpEvent = loadCSharpGeneratedEvent();
    const tsEvent = EventSerializer.deserialize(csharpEvent);
    
    // TypeScriptで再シリアライズ
    const reserialized = EventSerializer.serialize(tsEvent);
    
    // 構造の一致を確認
    expect(JSON.parse(reserialized)).toEqual(JSON.parse(csharpEvent));
  });
  
  test('Compression compatibility', async () => {
    // C#で圧縮されたデータを解凍
    const csharpCompressed = loadCSharpCompressedData();
    const decompressed = await CompressionService.decompress(csharpCompressed);
    
    // 期待値と比較
    expect(decompressed).toEqual(expectedData);
  });
});
```

### 6.2 パフォーマンステスト
```typescript
describe('Performance Tests', () => {
  test('Event processing throughput', async () => {
    const events = generateEvents(10000);
    const startTime = process.hrtime.bigint();
    
    await eventProcessor.processEvents(events);
    
    const endTime = process.hrtime.bigint();
    const durationMs = Number(endTime - startTime) / 1_000_000;
    
    // 目標: 10,000イベントを1秒以内に処理
    expect(durationMs).toBeLessThan(1000);
  });
});
```

## 7. 実装上の推奨事項

### 7.1 段階的実装アプローチ
1. **フェーズ1の最小実装**
   - 基本的なアクタープロキシラッパー
   - シンプルなイベントのシリアライズ/デシリアライズ
   - 基本的なエラーハンドリング

2. **フェーズ2での機能拡張**
   - バッチ処理の実装
   - キャッシング層の追加
   - 詳細なエラーハンドリング

3. **フェーズ3での最適化**
   - パフォーマンスチューニング
   - 高度なキャッシング戦略
   - モニタリング統合

### 7.2 ドキュメント戦略
```typescript
// JSDocによる型情報の提供
/**
 * Sekiban Event Store Client for TypeScript
 * @example
 * ```typescript
 * const client = new SekibanClient({
 *   daprPort: 3500,
 *   actorType: 'AggregateEventHandlerActor'
 * });
 * 
 * const events = await client.getEvents('user-123');
 * ```
 */
export class SekibanClient {
  // 実装
}
```

## 8. リスク管理

### 8.1 技術的リスク
| リスク | 影響度 | 発生確率 | 対策 |
|-------|--------|----------|------|
| シリアライゼーション非互換 | 高 | 中 | 包括的なテストスイート |
| パフォーマンス劣化 | 中 | 中 | ベンチマークとプロファイリング |
| バージョン非互換 | 高 | 低 | スキーマバージョニング |

### 8.2 運用リスク
- **監視**: OpenTelemetryとの統合を推奨
- **ロギング**: 構造化ログの実装
- **デバッグ**: ソースマップの適切な設定

## 9. 結論と次のステップ

### 9.1 総合評価
提案されたハイブリッドアプローチは、技術的に健全で実装可能です。特に以下の点が優れています：
- 実装リスクの最小化
- 段階的な移行パス
- 既存資産の活用

### 9.2 推奨される次のステップ
1. **プロトタイプ実装** (1週間)
   - 基本的なアクタープロキシ
   - シンプルなシリアライゼーション
   - 基本的な統合テスト

2. **技術検証** (1週間)
   - パフォーマンステスト
   - 相互運用性テスト
   - セキュリティ評価

3. **本実装開始**
   - フェーズ1の実装
   - ドキュメント作成
   - サンプルコード作成

## 10. 追加の技術的推奨事項

### 10.1 CI/CD統合
```yaml
# GitHub Actions例
name: Sekiban TypeScript CI
on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-node@v3
        with:
          node-version: '20'
      - run: npm ci
      - run: npm test
      - run: npm run lint
      - run: npm run build
```

### 10.2 パッケージ公開戦略
- npmパッケージとして公開
- セマンティックバージョニングの厳守
- CHANGELOGの維持
- 型定義ファイルの同梱

この設計により、TypeScriptユーザーがSekibanの強力なイベントソーシング機能を活用できるようになり、既存のC#エコシステムとの seamless な統合が実現されます。