# Sekiban TypeScript版 Result型設計計画書

## 概要

TypeScript版SekibanにおけるResult型の設計方針と実装計画を定義します。
C#版のResultBoxとは異なり、TypeScriptエコシステムで標準的な`neverthrow`ライブラリを採用し、TypeScript開発者にとって自然なエラーハンドリングを実現します。

## 設計原則

### 1. TypeScriptファースト
- C#版のAPIとの互換性は追求しない
- TypeScript開発者にとって自然で慣用的なAPI設計
- TypeScriptの型システムを最大限活用

### 2. データ互換性の確保
- イベントストアに保存されるデータ形式のみC#版と互換性を維持
- APIレベルでの互換性は不要
- 必要に応じて相互運用レイヤーを提供

### 3. 型安全性の重視
- コンパイル時にエラーを検出
- 実行時エラーを最小化
- 開発者体験（DX）の向上

## 技術選定：neverthrow

### 選定理由

1. **TypeScriptコミュニティでの標準**
   - 多くのTypeScriptプロジェクトで採用実績
   - 活発なメンテナンスとコミュニティ
   - 豊富なドキュメントとサンプル

2. **優れた型推論**
   - ジェネリクスが自動的に推論される
   - チェーン操作でも型情報が保持される
   - IDEの補完が効果的に機能

3. **非同期処理との自然な統合**
   - ResultAsyncでPromiseを扱いやすい
   - async/awaitパターンとの相性が良い
   - ストリーミング処理にも対応可能

4. **関数型プログラミングパラダイム**
   - Railway Oriented Programming
   - 副作用の分離
   - テスタブルなコード

### 基本的な使用例

```typescript
import { Result, ResultAsync, ok, err, okAsync, errAsync } from 'neverthrow'

// 同期的な処理
function divide(a: number, b: number): Result<number, string> {
  if (b === 0) {
    return err('Division by zero')
  }
  return ok(a / b)
}

// 非同期処理
function fetchUser(id: string): ResultAsync<User, ApiError> {
  return ResultAsync.fromPromise(
    fetch(`/api/users/${id}`).then(res => res.json()),
    (error) => new ApiError('Failed to fetch user', error)
  )
}

// チェーン処理
const result = await fetchUser('123')
  .andThen(user => validateUser(user))
  .map(user => enrichUserData(user))
  .mapErr(error => {
    console.error('Error occurred:', error)
    return error
  })

// パターンマッチング
result.match(
  (user) => console.log('Success:', user),
  (error) => console.error('Error:', error)
)
```

## Sekiban固有の型定義

### エラー型の階層

```typescript
// 基底エラー型
export interface SekibanError {
  readonly code: string
  readonly message: string
  readonly timestamp: Date
  readonly context?: Record<string, unknown>
}

// ドメインエラー
export type DomainError = 
  | ValidationError
  | BusinessRuleError
  | ConcurrencyError
  | AuthorizationError

export interface ValidationError extends SekibanError {
  readonly code: 'VALIDATION_ERROR'
  readonly field?: string
  readonly constraint: string
}

export interface BusinessRuleError extends SekibanError {
  readonly code: 'BUSINESS_RULE_ERROR'
  readonly rule: string
}

export interface ConcurrencyError extends SekibanError {
  readonly code: 'CONCURRENCY_ERROR'
  readonly expectedVersion: number
  readonly actualVersion: number
}

export interface AuthorizationError extends SekibanError {
  readonly code: 'AUTHORIZATION_ERROR'
  readonly requiredPermission?: string
}

// インフラストラクチャエラー
export type InfrastructureError =
  | StorageError
  | NetworkError
  | SerializationError

export interface StorageError extends SekibanError {
  readonly code: 'STORAGE_ERROR'
  readonly operation: 'read' | 'write' | 'delete'
}

export interface NetworkError extends SekibanError {
  readonly code: 'NETWORK_ERROR'
  readonly statusCode?: number
}

export interface SerializationError extends SekibanError {
  readonly code: 'SERIALIZATION_ERROR'
  readonly targetType?: string
}
```

### Result型エイリアス

```typescript
// コマンド実行結果
export type CommandResult<T> = ResultAsync<T, DomainError | InfrastructureError>

// クエリ実行結果
export type QueryResult<T> = ResultAsync<T, InfrastructureError>

// 集約ロード結果
export type AggregateResult<T> = ResultAsync<T, ConcurrencyError | StorageError>

// イベント保存結果
export type EventSaveResult = ResultAsync<void, StorageError | SerializationError>
```

## 実装パターン

### コマンドハンドラー

```typescript
export interface ICommandHandler<TCommand, TEvent> {
  handle(command: TCommand): CommandResult<TEvent>
}

export class CreateUserHandler implements ICommandHandler<CreateUserCommand, UserCreatedEvent> {
  constructor(
    private readonly userRepo: IUserRepository,
    private readonly emailValidator: IEmailValidator
  ) {}

  handle(command: CreateUserCommand): CommandResult<UserCreatedEvent> {
    // バリデーション
    return this.emailValidator.validate(command.email)
      .andThen(() => this.userRepo.exists(command.email))
      .andThen(exists => 
        exists 
          ? err(this.duplicateUserError(command.email))
          : ok(undefined)
      )
      .map(() => this.createEvent(command))
      .andThen(event => this.saveEvent(event))
  }

  private duplicateUserError(email: string): BusinessRuleError {
    return {
      code: 'BUSINESS_RULE_ERROR',
      message: `User with email ${email} already exists`,
      rule: 'UNIQUE_EMAIL',
      timestamp: new Date()
    }
  }

  private createEvent(command: CreateUserCommand): UserCreatedEvent {
    return {
      aggregateId: generateId(),
      type: 'UserCreated',
      payload: {
        email: command.email,
        name: command.name,
        createdAt: new Date()
      }
    }
  }

  private saveEvent(event: UserCreatedEvent): EventSaveResult {
    return ResultAsync.fromPromise(
      this.eventStore.save(event),
      (error) => ({
        code: 'STORAGE_ERROR',
        message: 'Failed to save event',
        operation: 'write',
        timestamp: new Date(),
        context: { error }
      })
    )
  }
}
```

### 集約ローダー

```typescript
export class AggregateLoader {
  constructor(private readonly eventStore: IEventStore) {}

  load<T extends IAggregatePayload>(
    aggregateId: string,
    projector: IAggregateProjector<T>
  ): AggregateResult<Aggregate<T>> {
    return this.eventStore.getEvents(aggregateId)
      .andThen(events => this.projectEvents(events, projector))
      .map(payload => ({
        id: aggregateId,
        payload,
        version: events.length
      }))
  }

  private projectEvents<T>(
    events: Event[],
    projector: IAggregateProjector<T>
  ): Result<T, never> {
    const initialPayload = projector.getInitialPayload()
    const payload = events.reduce(
      (acc, event) => projector.applyEvent(acc, event),
      initialPayload
    )
    return ok(payload)
  }
}
```

### クエリハンドラー

```typescript
export interface IQueryHandler<TQuery, TResult> {
  handle(query: TQuery): QueryResult<TResult>
}

export class GetUserByEmailHandler implements IQueryHandler<GetUserByEmailQuery, UserView> {
  constructor(private readonly readModel: IUserReadModel) {}

  handle(query: GetUserByEmailQuery): QueryResult<UserView> {
    return ResultAsync.fromPromise(
      this.readModel.findByEmail(query.email),
      (error) => ({
        code: 'STORAGE_ERROR',
        message: 'Failed to query user',
        operation: 'read',
        timestamp: new Date(),
        context: { error }
      })
    ).andThen(user => 
      user 
        ? ok(user)
        : err({
            code: 'STORAGE_ERROR',
            message: `User not found: ${query.email}`,
            operation: 'read',
            timestamp: new Date()
          })
    )
  }
}
```

## テスト戦略

### Result型のテスト

```typescript
import { describe, it, expect } from 'vitest'
import { ok, err } from 'neverthrow'

describe('CreateUserHandler', () => {
  const handler = new CreateUserHandler(mockRepo, mockValidator)

  it('should return error when email is invalid', async () => {
    const command = { email: 'invalid', name: 'Test' }
    const result = await handler.handle(command)

    expect(result.isErr()).toBe(true)
    expect(result._unsafeUnwrapErr()).toMatchObject({
      code: 'VALIDATION_ERROR',
      field: 'email'
    })
  })

  it('should return success when user is created', async () => {
    const command = { email: 'test@example.com', name: 'Test' }
    const result = await handler.handle(command)

    expect(result.isOk()).toBe(true)
    const event = result._unsafeUnwrap()
    expect(event.type).toBe('UserCreated')
    expect(event.payload.email).toBe(command.email)
  })

  it('should handle async errors gracefully', async () => {
    mockRepo.save.mockRejectedValue(new Error('Network error'))
    
    const command = { email: 'test@example.com', name: 'Test' }
    const result = await handler.handle(command)

    expect(result.isErr()).toBe(true)
    expect(result._unsafeUnwrapErr().code).toBe('STORAGE_ERROR')
  })
})
```

### テストユーティリティ

```typescript
// テスト用のResult生成ヘルパー
export const TestResult = {
  ok<T>(value: T): Result<T, never> {
    return ok(value)
  },
  
  err<E extends SekibanError>(error: Partial<E> & { code: string }): Result<never, E> {
    return err({
      message: 'Test error',
      timestamp: new Date(),
      ...error
    } as E)
  },
  
  okAsync<T>(value: T): ResultAsync<T, never> {
    return okAsync(value)
  },
  
  errAsync<E extends SekibanError>(error: Partial<E> & { code: string }): ResultAsync<never, E> {
    return errAsync({
      message: 'Test error',
      timestamp: new Date(),
      ...error
    } as E)
  }
}

// Result型のアサーション
export function expectOk<T, E>(result: Result<T, E>): T {
  expect(result.isOk()).toBe(true)
  return result._unsafeUnwrap()
}

export function expectErr<T, E>(result: Result<T, E>): E {
  expect(result.isErr()).toBe(true)
  return result._unsafeUnwrapErr()
}
```

## 相互運用性

### C#版との相互運用

イベントストアレベルでのみ互換性を保証：

```typescript
// C#版のイベント形式
interface CSharpStoredEvent {
  AggregateId: string
  PartitionKey: string
  EventType: string
  EventPayload: string  // JSON文字列
  SortableUniqueId: string
  CreatedAt: string     // ISO 8601
  Version: number
}

// 変換アダプター
export class EventFormatAdapter {
  toCSharpFormat(event: Event): CSharpStoredEvent {
    return {
      AggregateId: event.aggregateId,
      PartitionKey: event.partitionKey,
      EventType: event.type,
      EventPayload: JSON.stringify(event.payload),
      SortableUniqueId: event.sortableUniqueId,
      CreatedAt: event.createdAt.toISOString(),
      Version: event.version
    }
  }

  fromCSharpFormat(stored: CSharpStoredEvent): Result<Event, SerializationError> {
    try {
      return ok({
        aggregateId: stored.AggregateId,
        partitionKey: stored.PartitionKey,
        type: stored.EventType,
        payload: JSON.parse(stored.EventPayload),
        sortableUniqueId: stored.SortableUniqueId,
        createdAt: new Date(stored.CreatedAt),
        version: stored.Version
      })
    } catch (error) {
      return err({
        code: 'SERIALIZATION_ERROR',
        message: 'Failed to parse event from C# format',
        timestamp: new Date(),
        context: { error, stored }
      })
    }
  }
}
```

## 移行ガイドライン

### neverthrowへの移行

既存のPromiseベースのコードからの移行：

```typescript
// Before: Promiseベース
async function getUser(id: string): Promise<User> {
  try {
    const response = await fetch(`/api/users/${id}`)
    if (!response.ok) {
      throw new Error('User not found')
    }
    return await response.json()
  } catch (error) {
    console.error('Error:', error)
    throw error
  }
}

// After: neverthrowベース
function getUser(id: string): ResultAsync<User, ApiError> {
  return ResultAsync.fromPromise(
    fetch(`/api/users/${id}`),
    (error) => new ApiError('Network error', error)
  ).andThen(response => 
    response.ok
      ? ResultAsync.fromPromise(
          response.json(),
          () => new ApiError('Invalid response format')
        )
      : errAsync(new ApiError('User not found', { status: response.status }))
  )
}
```

## パフォーマンス考慮事項

### オーバーヘッド最小化

1. **Result型の再利用**
   ```typescript
   // 定数Result値をキャッシュ
   const VOID_OK = ok(undefined)
   const EMPTY_ARRAY_OK = ok([])
   ```

2. **不要なチェーンの回避**
   ```typescript
   // Bad: 不要なmap
   result.map(x => x).map(x => x)
   
   // Good: 直接返す
   result
   ```

3. **早期リターン**
   ```typescript
   // エラーの場合は早期リターン
   if (result.isErr()) {
     return result
   }
   ```

## まとめ

TypeScript版Sekibanでは、neverthrowを採用することで：

1. **型安全なエラーハンドリング** - 実行時エラーを型で表現
2. **優れた開発体験** - IDEサポートと明確なAPI
3. **非同期処理との統合** - ResultAsyncによる自然な非同期処理
4. **テスタビリティ** - 予測可能で testableなコード
5. **C#版との相互運用性** - データレベルでの互換性確保

これにより、TypeScript開発者にとって自然で、かつ堅牢なイベントソーシングフレームワークを実現します。