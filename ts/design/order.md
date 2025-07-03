# Sekiban TypeScript実装順序プラン - TDDアプローチ

## 分析結果

C#のSekiban.Pureコードを分析した結果、**依存関係の少ない基盤コンポーネントから段階的に実装**するのが最適です。

## Phase 1: 基盤ユーティリティ（第1週）
**最も独立性が高く、テストしやすい部分**

### 1.1 Date Producer (日付生成) 
**優先度: 最高**
- `DateProducer/SekibanDateProducer.cs` 
- 他の依存関係なし
- モック可能な設計
- 単純なInterface → Implementation

### 1.2 GUID Extensions
**優先度: 最高**
- `Extensions/GuidExtensions.cs`
- UUID v7サポート（Node.js環境では`uuid`ライブラリ）
- 完全に独立したユーティリティ

### 1.3 Validation Extensions
**優先度: 高**
- `Validations/ValidationExtensions.cs`
- バリデーション基盤
- Zodライブラリでの実装検討

## Phase 2: コアドキュメント型（第2週）
**フレームワークの基盤となるデータ構造**

### 2.1 SortableUniqueId
**優先度: 最高**
- `Documents/SortableUniqueIdValue.cs`
- 19桁tick + 11桁ランダム
- 文字列比較でソート可能
- 完全に独立したvalue object

### 2.2 PartitionKeys
**優先度: 最高**
- `Documents/PartitionKeys.cs`
- `AggregateId` + `Group` + `RootPartitionKey`
- マルチテナント対応の基盤
- SortableUniqueIdとGuid Extensionsに依存

## Phase 3: 基本インターフェース（第3週）
**型安全性の基盤**

### 3.1 Event基本型
**優先度: 最高**
- `Events/IEventPayload.cs` - マーカーインターフェース
- `Aggregates/IAggregatePayload.cs` - マーカーインターフェース
- 完全に独立

### 3.2 Event型定義
**優先度: 高**
- `Events/IEvent.cs`
- EventMetadata構造
- PartitionKeysに依存

## Phase 4: イベント管理（第4週）
**イベントストリーム基盤**

### 4.1 Event Document
**優先度: 高**
- `Events/EventDocument.cs`
- `Events/SerializableEventDocument.cs`
- シリアライゼーション対応

### 4.2 In-Memory Event Store
**優先度: 高**
- `Events/InMemoryEventReader.cs`
- `Events/InMemoryEventWriter.cs`
- `Repositories/Repository.cs`
- テスト用途とプロトタイピング

## Phase 5: 例外とエラーハンドリング（第5週）
**堅牢性の基盤**

### 5.1 ドメイン例外
**優先度: 中**
- `Exceptions/`配下の各例外クラス
- neverthrowとの統合

## Phase 6: Aggregate と Projector（第6週）
**状態管理の中核**

### 6.1 Aggregate基盤
**優先度: 最高**
- `Aggregates/IAggregate.cs`
- `Aggregates/Aggregate.cs`
- 不変性とバージョン管理

### 6.2 Projector システム
**優先度: 最高**
- `Aggregates/IAggregateProjector.cs`
- `Aggregates/IProjector.cs`
- パターンマッチングによる状態遷移

## Phase 7: コマンドハンドリング（第7週）
**ビジネスロジックの実装**

### 7.1 Command インターフェース
**優先度: 高**
- `Commands/ICommand.cs`
- `Commands/ICommandWithHandler.cs`
- コマンドバリデーション

### 7.2 Command Handler
**優先度: 高**
- `Commands/ICommandHandler.cs`
- `Commands/CommandHandler.cs`
- EventOrNone パターン

## 実装戦略の利点

### 1. **段階的検証**
- 各フェーズで独立したテストが可能
- 下位レイヤーの動作確認後、上位レイヤー実装

### 2. **早期フィードバック**
- SortableUniqueIdとPartitionKeysが完成すれば、基本的なイベント管理が動作
- 各週末に動作するコンポーネントが完成

### 3. **TDD最適化**
- Red-Green-Refactorサイクルを小さな単位で実行
- 依存関係が少ないため、モックの複雑度が低い

### 4. **TypeScript適応**
- C#のrecord型 → TypeScript interface + immutable object
- GUID → string（UUIDライブラリ使用）
- DateTime → Date + ユーティリティ関数
- ResultBox → neverthrow Result型

## 詳細実装ガイド

### Phase 1.1: Date Producer実装詳細

C#実装:
```csharp
public class SekibanDateProducer : ISekibanDateProducer
{
    private static ISekibanDateProducer _registered = new SekibanDateProducer();
    public DateTime Now => DateTime.Now;
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Today => DateTime.Today;

    public static ISekibanDateProducer GetRegistered() => _registered;
    public static void Register(ISekibanDateProducer sekibanDateProducer) => _registered = sekibanDateProducer;
}
```

TypeScript実装方針:
```typescript
interface ISekibanDateProducer {
  now(): Date;
  utcNow(): Date;
  today(): Date;
}

class SekibanDateProducer implements ISekibanDateProducer {
  private static registered: ISekibanDateProducer = new SekibanDateProducer();
  
  now(): Date { return new Date(); }
  utcNow(): Date { return new Date(); }
  today(): Date { 
    const date = new Date();
    return new Date(date.getFullYear(), date.getMonth(), date.getDate());
  }
  
  static getRegistered(): ISekibanDateProducer { return this.registered; }
  static register(producer: ISekibanDateProducer): void { this.registered = producer; }
}
```

### Phase 2.1: SortableUniqueId実装詳細

C#実装の特徴:
- 19桁tick（DateTime.Ticksベース）
- 11桁ランダム（GuidのHashCodeベース）
- 文字列比較でソート可能

TypeScript実装課題:
- JavaScriptにはTicksがない → `Date.now() * 10000` + 調整
- HashCodeがない → UUID v4の数値化
- 精密な文字列フォーマットが必要

### Phase 2.2: PartitionKeys実装詳細

C#実装の特徴:
- Record型による不変性
- バリデーション属性
- ジェネリック静的クラス

TypeScript実装方針:
- readonly interfaceによる不変性
- Zodバリデーション
- 静的ファクトリーメソッド

## TDD実装手順

### 各フェーズ共通プロセス

1. **RED**: 失敗するテストケースを作成
   ```typescript
   describe('SekibanDateProducer', () => {
     it('should return current date', () => {
       const producer = new SekibanDateProducer();
       const now = producer.now();
       expect(now).toBeInstanceOf(Date);
     });
   });
   ```

2. **GREEN**: 最小限の実装でテストを通す
   ```typescript
   class SekibanDateProducer {
     now(): Date {
       return new Date();
     }
   }
   ```

3. **REFACTOR**: コードの改善とテストの追加
   ```typescript
   // テスト時間固定、エラーケース、境界値テストの追加
   ```

### モック戦略

各フェーズでDependency Injectionを活用:
```typescript
// Date Producerのモック例
const mockDateProducer: ISekibanDateProducer = {
  now: () => new Date('2024-01-01T00:00:00Z'),
  utcNow: () => new Date('2024-01-01T00:00:00Z'),
  today: () => new Date('2024-01-01T00:00:00Z')
};
```

## Phase 6: 集約とプロジェクター（第6週）
**イベントソーシングの中核**

### 6.1 Aggregate基本型
**優先度: 最高**
- `Aggregates/IAggregatePayload.cs` - マーカーインターフェース（Phase 3で実装済み）
- `Aggregates/IAggregate.cs` - 集約の基本インターフェース
  - Version: 集約のバージョン番号
  - LastSortableUniqueId: 最後に適用されたイベントのID
  - PartitionKeys: 集約の識別子
  - ProjectorVersion: プロジェクターのバージョン
  - ProjectorTypeName: プロジェクターの型名
  - PayloadTypeName: ペイロードの型名

### 6.2 Aggregate実装
**優先度: 最高**
- `Aggregates/Aggregate.cs` - 集約の具体実装
  - Record型による不変性
  - Projectメソッド: イベントを適用して新しい状態を生成
  - 型安全なペイロード変換
  - EmptyAggregateの定義

### 6.3 プロジェクター基本型
**優先度: 最高**
- `Projectors/IAggregateProjector.cs` - プロジェクターインターフェース
  - Project(payload, event): イベントを適用して新しいペイロードを返す
  - GetVersion(): プロジェクターのバージョンを返す
  - **重要**: プロジェクターはステートレスで純粋関数的

### 6.4 プロジェクターの実装パターン
**優先度: 高**
プロジェクターは以下のパターンでイベントを処理:
```csharp
public class UserProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) => 
        (payload, ev.GetPayload()) switch
        {
            // 初期状態からのイベント適用
            (EmptyAggregatePayload, UserRegistered registered) => 
                new UnconfirmedUser(registered.Name, registered.Email),
            
            // 状態遷移
            (UnconfirmedUser unconfirmed, UserConfirmed) => 
                new ConfirmedUser(unconfirmed.Name, unconfirmed.Email),
            
            // 状態内の更新
            (ConfirmedUser confirmed, UserNameUpdated updated) => 
                confirmed with { Name = updated.NewName },
            
            // デフォルト（未処理のイベントは状態を変更しない）
            _ => payload
        };
}
```

### 6.5 マルチプロジェクター
**優先度: 中**
- `Projectors/IMultiProjector.cs` - 複数の集約を横断するプロジェクター
- `Projectors/MultiProjectionState.cs` - マルチプロジェクションの状態管理
- 複数の集約からのイベントを統合して読み取りモデルを構築

## Phase 7: コマンドハンドリング（第7週）
**コマンド処理とイベント生成**

### 7.1 コマンド基本型
**優先度: 最高**
- `Command/ICommand.cs` - コマンドマーカーインターフェース
- `Command/Handlers/ICommandHandler.cs` - コマンドハンドラー
  - Handle(command, context): EventOrNoneを返す
  - コンテキストから現在の集約状態を取得

### 7.2 CommandWithHandler
**優先度: 最高**
- `Command/Handlers/ICommandWithHandler.cs` - コマンドとハンドラーの統合インターフェース
  - SpecifyPartitionKeys: コマンドからPartitionKeysを生成
  - Handle: コマンドを処理してイベントを生成
  - GetProjector: 関連するプロジェクターを返す

### 7.3 EventOrNone
**優先度: 高**
- イベント生成結果の表現
- イベントが生成される場合とされない場合の両方をサポート
- ResultBoxとの統合

## Phase 8: クエリ処理（第8週）
**読み取りモデルとクエリ**

## Phase 9: SekibanExecutor（第9週）
**実行エンジン**

## Phase 10: ストレージプロバイダー統合（第10週）
**永続化層**

## 実装のポイント（Phase 6）

### TypeScript実装での考慮事項

1. **パターンマッチング**
   - C#のswitch式 → TypeScriptのif-else チェーンまたはパターンマッチングライブラリ
   - 型ガードを活用した安全な型判定

2. **不変性**
   - C#のrecord with式 → TypeScriptのspread演算子とreadonly
   - Immerライブラリの活用検討

3. **プロジェクターの純粋性**
   - 副作用なし
   - 同じ入力に対して常に同じ出力
   - テストが容易

4. **集約の状態遷移**
   - 型レベルで有効な状態遷移を表現
   - 無効な状態遷移はコンパイル時エラー

### テスト戦略（Phase 6）

```typescript
describe('UserProjector', () => {
  it('should create unconfirmed user from UserRegistered event', () => {
    const projector = new UserProjector();
    const event = createEvent(new UserRegistered('John', 'john@example.com'));
    
    const result = projector.project(EmptyAggregatePayload, event);
    
    expect(result).toEqual(new UnconfirmedUser('John', 'john@example.com'));
  });
  
  it('should transition from unconfirmed to confirmed', () => {
    const projector = new UserProjector();
    const unconfirmed = new UnconfirmedUser('John', 'john@example.com');
    const event = createEvent(new UserConfirmed());
    
    const result = projector.project(unconfirmed, event);
    
    expect(result).toEqual(new ConfirmedUser('John', 'john@example.com'));
  });
});
```

## 推奨開始点

**Phase 1.1 Date Producer**から開始することを強く推奨します。
- 最もシンプルで理解しやすい
- 他の全てのコンポーネントが依存する基盤
- TDDサイクルの練習に最適
- モック作成が容易