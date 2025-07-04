# Sekiban TypeScript版 TDD開発プロセス

## 概要

このドキュメントでは、t_wada氏のTDD手法に基づいたSekiban TypeScript版の開発プロセスを定義します。
テストファーストアプローチにより、高品質で保守性の高いイベントソーシングフレームワークを実現します。

## TDDの基本原則

### 1. Red-Green-Refactorサイクル

```
1. Red: 失敗するテストを書く
2. Green: テストを通す最小限の実装
3. Refactor: コードを改善（テストは通ったまま）
```

### 2. 小さなステップ（Baby Steps）

- 一度に大きな変更を加えない
- 頻繁にコミット（各Greenフェーズ後）
- 常に動作する状態を維持

### 3. 三角測量（Triangulation）

- 複数の具体例から一般化を導く
- 最初は具体的な実装、徐々に汎用化
- 早すぎる抽象化を避ける

## 開発環境のセットアップ

### テスト環境の構築

```bash
# プロジェクト初期化
pnpm init
pnpm add -D vitest @vitest/ui @vitest/coverage-v8
pnpm add -D typescript @types/node
pnpm add -D prettier eslint @typescript-eslint/parser @typescript-eslint/eslint-plugin
```

### vitest.config.ts

```typescript
import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    globals: true,
    environment: 'node',
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      exclude: [
        'node_modules/',
        'tests/',
        '**/*.d.ts',
        '**/*.config.*',
        '**/index.ts'
      ],
      thresholds: {
        lines: 90,
        functions: 90,
        branches: 85,
        statements: 90
      }
    },
    watchExclude: ['**/node_modules/**', '**/dist/**']
  }
})
```

### package.jsonのスクリプト

```json
{
  "scripts": {
    "test": "vitest",
    "test:ui": "vitest --ui",
    "test:run": "vitest run",
    "test:coverage": "vitest run --coverage",
    "test:watch": "vitest --watch"
  }
}
```

## 実装順序とTDDアプローチ

### Phase 1: 基本的な値オブジェクト（第1週）

#### 1.1 SortableUniqueIdの実装

```typescript
// tests/unit/documents/sortable-unique-id.test.ts
import { describe, it, expect } from 'vitest'
import { SortableUniqueId } from '../../../src/packages/core/documents/sortable-unique-id'

describe('SortableUniqueId', () => {
  // RED: 最初のテスト
  it('should create a unique id', () => {
    const id1 = SortableUniqueId.generate()
    const id2 = SortableUniqueId.generate()
    
    expect(id1.value).not.toBe(id2.value)
  })
  
  // RED: 時系列順序のテスト
  it('should create sortable ids', async () => {
    const id1 = SortableUniqueId.generate()
    await new Promise(resolve => setTimeout(resolve, 10))
    const id2 = SortableUniqueId.generate()
    
    expect(id1.value < id2.value).toBe(true)
  })
  
  // RED: 文字列変換のテスト
  it('should convert to string', () => {
    const id = SortableUniqueId.generate()
    
    expect(id.toString()).toBe(id.value)
  })
})
```

実装の進め方：
1. 最小限の実装でテストを通す
2. リファクタリングで改善
3. 新しいテストケースを追加

#### 1.2 PartitionKeysの実装

```typescript
// tests/unit/documents/partition-keys.test.ts
describe('PartitionKeys', () => {
  // AAA (Arrange-Act-Assert) パターンの使用
  it('should create partition keys with aggregate id', () => {
    // Arrange
    const aggregateId = 'user-123'
    
    // Act
    const keys = PartitionKeys.create(aggregateId)
    
    // Assert
    expect(keys.aggregateId).toBe(aggregateId)
    expect(keys.partitionKey).toBe(aggregateId)
  })
  
  // 三角測量：複数のケースから一般化
  it('should create partition keys with group', () => {
    const keys = PartitionKeys.createWithGroup('user-123', 'users')
    
    expect(keys.aggregateId).toBe('user-123')
    expect(keys.group).toBe('users')
    expect(keys.partitionKey).toBe('users-user-123')
  })
  
  it('should create partition keys with root partition', () => {
    const keys = PartitionKeys.createWithRoot('user-123', 'users', 'tenant-1')
    
    expect(keys.rootPartitionKey).toBe('tenant-1')
    expect(keys.partitionKey).toBe('tenant-1-users-user-123')
  })
})
```

### Phase 2: Result型とエラーハンドリング（第2週）

#### 2.1 Result型の実装（neverthrowを使用）

```typescript
// tests/unit/result/result.test.ts
import { describe, it, expect } from 'vitest'
import { ok, err, Result } from 'neverthrow'
import { DomainError, ValidationError } from '../../../src/packages/core/result/errors'

describe('Result type with neverthrow', () => {
  // 成功ケースのテスト
  it('should handle success case', () => {
    const result: Result<number, Error> = ok(42)
    
    expect(result.isOk()).toBe(true)
    expect(result.isErr()).toBe(false)
    expect(result._unsafeUnwrap()).toBe(42)
  })
  
  // エラーケースのテスト
  it('should handle error case', () => {
    const error = new ValidationError('Invalid input', 'email')
    const result: Result<number, ValidationError> = err(error)
    
    expect(result.isErr()).toBe(true)
    expect(result._unsafeUnwrapErr().field).toBe('email')
  })
  
  // チェーン操作のテスト
  it('should chain operations', () => {
    const result = ok(10)
      .map(x => x * 2)
      .andThen(x => x > 15 ? ok(x) : err('Too small'))
    
    expect(result._unsafeUnwrap()).toBe(20)
  })
})
```

#### 2.2 ドメインエラーの実装

```typescript
// tests/unit/result/domain-errors.test.ts
describe('Domain Errors', () => {
  it('should create validation error', () => {
    const error = new ValidationError('Email is invalid', 'email')
    
    expect(error.code).toBe('VALIDATION_ERROR')
    expect(error.field).toBe('email')
    expect(error.message).toBe('Email is invalid')
  })
  
  it('should create business rule error', () => {
    const error = new BusinessRuleError('Insufficient balance', 'MIN_BALANCE')
    
    expect(error.code).toBe('BUSINESS_RULE_ERROR')
    expect(error.rule).toBe('MIN_BALANCE')
  })
})
```

### Phase 3: イベントシステム（第3週）

#### 3.1 イベントペイロードの実装

```typescript
// tests/unit/events/event-payload.test.ts
describe('Event Payload', () => {
  // 具体的なイベントから始める
  it('should create UserCreated event', () => {
    // Arrange
    const payload = {
      userId: 'user-123',
      email: 'test@example.com',
      name: 'Test User'
    }
    
    // Act
    const event = new UserCreated(payload)
    
    // Assert
    expect(event.userId).toBe(payload.userId)
    expect(event.email).toBe(payload.email)
    expect(event.name).toBe(payload.name)
  })
  
  // 2つ目のイベントで共通性を見出す
  it('should create UserUpdated event', () => {
    const event = new UserUpdated({
      userId: 'user-123',
      name: 'Updated Name'
    })
    
    expect(event.userId).toBe('user-123')
  })
  
  // インターフェースの抽出（リファクタリング後）
  it('should implement IEventPayload interface', () => {
    const event: IEventPayload = new UserCreated({
      userId: 'user-123',
      email: 'test@example.com'
    })
    
    expect(isEventPayload(event)).toBe(true)
  })
})
```

#### 3.2 イベントドキュメントの実装

```typescript
// tests/unit/events/event-document.test.ts
describe('EventDocument', () => {
  // シリアライゼーションのテスト
  it('should serialize event to storage format', () => {
    // Arrange
    const event = new UserCreated({
      userId: 'user-123',
      email: 'test@example.com'
    })
    const partitionKeys = PartitionKeys.create('user-123')
    
    // Act
    const document = EventDocument.create(event, partitionKeys)
    const serialized = document.toStorageFormat()
    
    // Assert
    expect(serialized.AggregateId).toBe('user-123')
    expect(serialized.EventType).toBe('UserCreated')
    expect(JSON.parse(serialized.EventPayload)).toEqual({
      userId: 'user-123',
      email: 'test@example.com'
    })
  })
  
  // デシリアライゼーションのテスト
  it('should deserialize from storage format', () => {
    const stored = {
      AggregateId: 'user-123',
      EventType: 'UserCreated',
      EventPayload: '{"userId":"user-123","email":"test@example.com"}',
      SortableUniqueId: 'id-123',
      CreatedAt: new Date().toISOString(),
      Version: 1
    }
    
    const result = EventDocument.fromStorageFormat(stored)
    
    expect(result.isOk()).toBe(true)
    const document = result._unsafeUnwrap()
    expect(document.aggregateId).toBe('user-123')
  })
})
```

### Phase 4: 集約とプロジェクター（第4週）

#### 4.1 プロジェクターの実装

```typescript
// tests/unit/aggregates/user-projector.test.ts
describe('UserProjector', () => {
  let projector: UserProjector
  
  beforeEach(() => {
    projector = new UserProjector()
  })
  
  // 初期状態のテスト
  it('should have initial state', () => {
    const initial = projector.getInitialPayload()
    
    expect(initial.status).toBe('inactive')
    expect(initial.email).toBeUndefined()
  })
  
  // イベント適用のテスト（AAA パターン）
  it('should apply UserCreated event', () => {
    // Arrange
    const event = new UserCreated({
      userId: 'user-123',
      email: 'test@example.com',
      name: 'Test User'
    })
    const initial = projector.getInitialPayload()
    
    // Act
    const newState = projector.applyEvent(initial, event)
    
    // Assert
    expect(newState.status).toBe('active')
    expect(newState.email).toBe('test@example.com')
    expect(newState.name).toBe('Test User')
  })
  
  // 複数イベントの適用（イベントソーシングの本質）
  it('should apply multiple events in sequence', () => {
    const events = [
      new UserCreated({ userId: 'user-123', email: 'test@example.com' }),
      new UserUpdated({ userId: 'user-123', name: 'Updated Name' }),
      new UserEmailVerified({ userId: 'user-123' })
    ]
    
    const finalState = events.reduce(
      (state, event) => projector.applyEvent(state, event),
      projector.getInitialPayload()
    )
    
    expect(finalState.name).toBe('Updated Name')
    expect(finalState.emailVerified).toBe(true)
  })
})
```

### Phase 5: コマンドハンドリング（第5週）

#### 5.1 コマンドハンドラーの実装

```typescript
// tests/unit/commands/create-user-handler.test.ts
describe('CreateUserHandler', () => {
  let handler: CreateUserHandler
  let userRepository: MockUserRepository
  
  beforeEach(() => {
    userRepository = new MockUserRepository()
    handler = new CreateUserHandler(userRepository)
  })
  
  // 成功ケースのテスト
  it('should create user successfully', async () => {
    // Arrange
    const command = new CreateUserCommand({
      email: 'test@example.com',
      name: 'Test User'
    })
    userRepository.exists.mockResolvedValue(false)
    
    // Act
    const result = await handler.handle(command)
    
    // Assert
    expect(result.isOk()).toBe(true)
    const event = result._unsafeUnwrap()
    expect(event).toBeInstanceOf(UserCreated)
    expect(event.email).toBe('test@example.com')
  })
  
  // バリデーションエラーのテスト
  it('should return error for invalid email', async () => {
    const command = new CreateUserCommand({
      email: 'invalid-email',
      name: 'Test User'
    })
    
    const result = await handler.handle(command)
    
    expect(result.isErr()).toBe(true)
    const error = result._unsafeUnwrapErr()
    expect(error.code).toBe('VALIDATION_ERROR')
    expect(error.field).toBe('email')
  })
  
  // ビジネスルールエラーのテスト
  it('should return error for duplicate email', async () => {
    const command = new CreateUserCommand({
      email: 'existing@example.com',
      name: 'Test User'
    })
    userRepository.exists.mockResolvedValue(true)
    
    const result = await handler.handle(command)
    
    expect(result.isErr()).toBe(true)
    const error = result._unsafeUnwrapErr()
    expect(error.code).toBe('BUSINESS_RULE_ERROR')
    expect(error.rule).toBe('UNIQUE_EMAIL')
  })
})
```

## テスト戦略

### テストピラミッド

```
          /\
         /E2E\        10% - エンドツーエンドテスト
        /------\
       /統合テスト\    20% - 統合テスト
      /----------\
     /ユニットテスト\  70% - ユニットテスト
    /--------------\
```

### ユニットテスト

- 個々のクラス・関数の動作確認
- モックを活用した独立したテスト
- 高速実行（ミリ秒単位）

### 統合テスト

```typescript
// tests/integration/user-lifecycle.test.ts
describe('User Lifecycle Integration', () => {
  let executor: InMemorySekibanExecutor
  
  beforeEach(() => {
    executor = new InMemorySekibanExecutor()
  })
  
  it('should handle complete user lifecycle', async () => {
    // ユーザー作成
    const createResult = await executor.execute(
      new CreateUserCommand({
        email: 'test@example.com',
        name: 'Test User'
      })
    )
    expect(createResult.isOk()).toBe(true)
    const userId = createResult._unsafeUnwrap().aggregateId
    
    // ユーザー更新
    const updateResult = await executor.execute(
      new UpdateUserCommand({
        userId,
        name: 'Updated Name'
      })
    )
    expect(updateResult.isOk()).toBe(true)
    
    // 状態確認
    const query = new GetUserQuery(userId)
    const user = await executor.query(query)
    
    expect(user.name).toBe('Updated Name')
    expect(user.version).toBe(2)
  })
})
```

### E2Eテスト

```typescript
// tests/e2e/api-flow.test.ts
describe('API E2E Flow', () => {
  it('should process commands through HTTP API', async () => {
    // 実際のHTTPリクエスト
    const response = await fetch('http://localhost:3000/api/commands', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        type: 'CreateUser',
        payload: {
          email: 'e2e@example.com',
          name: 'E2E Test'
        }
      })
    })
    
    expect(response.status).toBe(200)
    const result = await response.json()
    expect(result.success).toBe(true)
  })
})
```

## テストヘルパーとユーティリティ

### テストビルダー

```typescript
// tests/helpers/builders.ts
export class UserEventBuilder {
  private payload: Partial<UserCreatedPayload> = {}
  
  withEmail(email: string): this {
    this.payload.email = email
    return this
  }
  
  withName(name: string): this {
    this.payload.name = name
    return this
  }
  
  build(): UserCreated {
    return new UserCreated({
      userId: this.payload.userId || 'test-user-id',
      email: this.payload.email || 'test@example.com',
      name: this.payload.name || 'Test User'
    })
  }
}

// 使用例
const event = new UserEventBuilder()
  .withEmail('custom@example.com')
  .build()
```

### カスタムマッチャー

```typescript
// tests/helpers/matchers.ts
import { expect } from 'vitest'

expect.extend({
  toBeValidEvent(received: unknown) {
    const pass = isEventPayload(received)
    return {
      pass,
      message: () => 
        pass 
          ? `Expected ${received} not to be a valid event`
          : `Expected ${received} to be a valid event`
    }
  }
})

// 使用例
expect(event).toBeValidEvent()
```

### スナップショットテスト

```typescript
// イベントのシリアライゼーション確認
it('should serialize event correctly', () => {
  const event = new UserCreated({
    userId: 'user-123',
    email: 'test@example.com',
    name: 'Test User'
  })
  
  expect(serialize(event)).toMatchSnapshot()
})
```

## 継続的なTDD実践

### コミット戦略

```bash
# Red フェーズ
git add tests/
git commit -m "test: add failing test for user creation"

# Green フェーズ
git add src/
git commit -m "feat: implement user creation to pass test"

# Refactor フェーズ
git add .
git commit -m "refactor: extract user validation logic"
```

### Git フック設定

```json
// package.json
{
  "scripts": {
    "pre-commit": "lint-staged",
    "pre-push": "pnpm test:run"
  },
  "lint-staged": {
    "*.ts": [
      "eslint --fix",
      "prettier --write",
      "vitest related --run"
    ]
  }
}
```

### カバレッジ目標

- 新規コード: 100%
- 全体: 90%以上
- クリティカルパス: 95%以上

## プロパティベーステスト

```typescript
import fc from 'fast-check'

describe('Property-based tests', () => {
  it('should maintain aggregate invariants', () => {
    fc.assert(
      fc.property(
        fc.array(fc.record({
          type: fc.constantFrom('deposit', 'withdraw'),
          amount: fc.integer({ min: 1, max: 1000 })
        })),
        (operations) => {
          const account = operations.reduce(
            (acc, op) => applyOperation(acc, op),
            { balance: 0 }
          )
          
          // 不変条件: 残高は負にならない
          return account.balance >= 0
        }
      )
    )
  })
})
```

## パフォーマンステスト

```typescript
// tests/performance/event-replay.bench.ts
import { bench, describe } from 'vitest'

describe('Event Replay Performance', () => {
  bench('replay 1000 events', () => {
    const events = generateEvents(1000)
    const projector = new AccountProjector()
    
    events.reduce(
      (state, event) => projector.applyEvent(state, event),
      projector.getInitialPayload()
    )
  })
  
  bench('replay 10000 events', () => {
    const events = generateEvents(10000)
    const projector = new AccountProjector()
    
    events.reduce(
      (state, event) => projector.applyEvent(state, event),
      projector.getInitialPayload()
    )
  })
})
```

## TDD実践のチェックリスト

### 各機能実装前

- [ ] 失敗するテストを書いたか？
- [ ] テストは明確で理解しやすいか？
- [ ] AAAパターンに従っているか？

### Green フェーズ後

- [ ] テストは通っているか？
- [ ] 実装は最小限か？
- [ ] コミットしたか？

### Refactor フェーズ後

- [ ] テストは依然として通っているか？
- [ ] コードは改善されたか？
- [ ] 新たなテストケースが必要か？

## まとめ

TDDによる開発により、以下を実現します：

1. **高品質なコード**: バグの早期発見と防止
2. **設計の改善**: テスタブルな設計は良い設計
3. **ドキュメント**: テストが仕様書として機能
4. **リファクタリングの安全性**: テストによる保護
5. **開発速度の向上**: 長期的には高速な開発が可能

t_wada氏の言葉：「テストを書くことは目的ではなく、品質の高いソフトウェアを素早く作ることが目的」を忘れずに、実践的なTDDを心がけましょう。