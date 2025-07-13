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

## Phase 8: クエリ処理（第8週）
**読み取りモデルの実装**

### 8.1 Query インターフェース
**優先度: 高**
- `Queries/IQuery.cs`
- `Queries/IQueryHandler.cs`
- 型安全なクエリ定義

### 8.2 Query Handler
**優先度: 高**
- `Queries/QueryHandler.cs`
- Aggregate読み取り
- Multi-Projection対応

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
**実行エンジン - コマンドとクエリの実行**

### 9.1 Executor基本インターフェース
**優先度: 最高**
- `ISekibanExecutor` - 統合実行インターフェース
  - CommandAsync: コマンドを実行してイベントを生成
  - QueryAsync: クエリを実行して結果を返す
  - 型安全なコマンド/クエリ実行
  - エラーハンドリングとリトライ

### 9.2 Command Executor
**優先度: 最高**
- `ICommandExecutor` - コマンド実行専用インターフェース
  - ExecuteCommand: コマンドハンドラーとプロジェクターの連携
  - バージョン一貫性の確保
  - 楽観的並行性制御
  - イベント保存とアグリゲート更新

### 9.3 Query Executor
**優先度: 高**
- `IQueryExecutor` - クエリ実行専用インターフェース
  - ExecuteQuery: 単一アグリゲートクエリ
  - ExecuteMultiProjectionQuery: マルチプロジェクションクエリ
  - ExecuteListQuery: リストクエリとページネーション
  - 読み取り専用操作

### 9.4 In-Memory Executor
**優先度: 高**
- `InMemorySekibanExecutor` - メモリ内実行エンジン
- テストとプロトタイピング用途
- 完全な機能実装
- 高速実行

## Phase 10: ストレージプロバイダー統合（第10週）
**永続化層**

### 10.1 Storage Provider インターフェース
**優先度: 最高**
- `IEventStorageProvider` - ストレージプロバイダーの基本インターフェース
  - SaveEvents: イベントの永続化
  - LoadEvents: イベントの読み込み
  - LoadEventsByPartitionKey: パーティションキーによるイベント読み込み
  - GetLatestSnapshot: 最新スナップショットの取得
  - SaveSnapshot: スナップショットの保存

### 10.2 Storage Configuration
**優先度: 最高**
- `StorageProviderConfig` - ストレージプロバイダーの設定
  - ConnectionString: 接続文字列
  - DatabaseName: データベース名
  - ContainerName/TableName: コンテナー/テーブル名
  - RetryPolicy: リトライポリシー
  - TimeoutSettings: タイムアウト設定

### 10.3 Cosmos DB Provider
**優先度: 高**
- `CosmosDbEventStorageProvider` - Azure Cosmos DB実装
  - パーティションキー戦略
  - 変更フィード対応
  - 楽観的並行性制御
  - バッチ書き込み最適化

### 10.4 PostgreSQL Provider
**優先度: 高**
- `PostgresEventStorageProvider` - PostgreSQL実装
  - JSONBを使用したイベント保存
  - インデックス戦略
  - トランザクション管理
  - イベントストリーミング対応

### 10.5 Storage Provider Factory
**優先度: 中**
- `StorageProviderFactory` - プロバイダーのファクトリー
  - 設定に基づくプロバイダー選択
  - プロバイダーの登録と解決
  - デフォルトプロバイダーの設定

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

## Phase 11: 永続ストレージ実装（第11週）
**本番環境対応の永続化層 - 優先度: 最高**

### 11.1 PostgreSQL Storage Provider
**優先度: 最高**
- 完全なPostgreSQL実装
  - 接続プーリング（pg-pool）
  - トランザクションサポート
  - JSONB型でのイベント保存
  - パーティションキーとタイムスタンプのインデックス
  - prepared statementによるSQLインジェクション対策

### 11.2 CosmosDB Storage Provider
**優先度: 最高**
- Azure Cosmos DB完全実装
  - パーティションキー最適化
  - 継続トークンによる大規模結果セット対応
  - Change Feed統合
  - RU（Request Unit）最適化
  - 指数バックオフによる自動リトライ

### 11.3 ストレージ移行ツール
**優先度: 高**
- スキーマバージョニング
- データ移行ユーティリティ
- ストレージプロバイダー切り替え機能
- バックアップ・リストア機能

## Phase 12: スナップショット管理（第12週）
**パフォーマンス最適化の中核 - 優先度: 高**

### 12.1 スナップショット戦略
**優先度: 高**
- 設定可能なスナップショット間隔
  - イベント数ベース
  - 時間ベース
  - サイズベース
- スナップショット圧縮（gzip/brotli）
- 遅延スナップショット生成
- スナップショットクリーンアップポリシー

### 12.2 スナップショットストレージ
**優先度: 高**
- 独立したスナップショットストレージオプション
- スナップショットバージョニング
- 並列スナップショットローディング
- スナップショットの整合性検証

### 12.3 パフォーマンス最適化
**優先度: 中**
- 自動スナップショットベースのリプレイ
- メモリ効率的なイベントリプレイ
- スナップショットキャッシング戦略
- スナップショット作成の非同期化

## Phase 13: イベントバージョニング＆スキーマ進化（第13週）
**長期的な保守性の確保 - 優先度: 最高**

### 13.1 イベントバージョニングシステム
**優先度: 最高**
- イベントスキーマレジストリ
  - スキーマ定義の一元管理
  - バージョン互換性チェック
  - 自動スキーマ検証
- イベントアップキャスト/ダウンキャスト
  - 旧バージョンから新バージョンへの変換
  - 型安全な変換ロジック

### 13.2 スキーマ進化パターン
**優先度: 高**
- フィールド追加/削除戦略
  - オプショナルフィールドの活用
  - デフォルト値の設定
- 型マイグレーションサポート
  - プリミティブ型の変換
  - 複合型の分解・統合
- 前方/後方互換性の確保

### 13.3 マイグレーションツール
**優先度: 中**
- イベント変換パイプライン
- バッチイベントマイグレーション
- バージョン競合解決
- マイグレーション進捗トラッキング

## Phase 14: テストフレームワーク＆開発者体験ツール（第14週）
**開発生産性の向上 - 優先度: 高**

### 14.1 テストユーティリティ
**優先度: 高**
- `SekibanTestBase` - ユニットテスト基底クラス
  - Given-When-Thenパターンサポート
  - 時間制御機能
  - イベント検証ヘルパー
- コマンド/クエリテストビルダー
  - Fluent APIでのテスト記述
  - 期待値の宣言的記述
- モックイベントストア
  - 決定的な動作
  - エラーシミュレーション

### 14.2 デバッグツール
**優先度: 中**
- イベントストリームビジュアライザー
  - 時系列表示
  - イベント相関の可視化
- アグリゲート状態インスペクター
  - 状態遷移の追跡
  - スナップショット比較
- コマンド/イベントトレーシング
  - 実行パスの記録
  - パフォーマンスボトルネック検出

### 14.3 開発者テンプレート
**優先度: 中**
- プロジェクトスキャフォールディングCLI
  - `npx create-sekiban-app`
  - 対話型セットアップ
- コードジェネレーター
  - アグリゲート生成
  - コマンド/イベント生成
- VS Code拡張
  - スニペット
  - 構文ハイライト
  - IntelliSense強化

## Phase 15: プロセスマネージャー＆サーガ（第15週）
**複雑なビジネスプロセスの実装 - 優先度: 高**

### 15.1 プロセスマネージャーフレームワーク
**優先度: 高**
- 長期実行プロセスの調整
  - ステートマシン実装
  - イベント駆動の状態遷移
- 補償ロジックサポート
  - ロールバック戦略
  - 部分的成功の処理
- タイムアウト処理
  - 設定可能なタイムアウト
  - タイムアウトイベント生成

### 15.2 サーガオーケストレーション
**優先度: 高**
- サーガ定義DSL
  - 宣言的なサーガ記述
  - タイプセーフなステップ定義
- ステップ実行エンジン
  - 順次/並列実行
  - 条件分岐
- ロールバックメカニズム
  - 補償トランザクション
  - エラーリカバリー

### 15.3 ワークフロー統合
**優先度: 中**
- イベント駆動ワークフロー
- 条件分岐サポート
- 並列実行サポート
- 外部システム連携

## Phase 16: モニタリング＆可観測性（第16週）
**運用監視の実現 - 優先度: 最高**

### 16.1 メトリクス収集
**優先度: 最高**
- コマンド/クエリパフォーマンスメトリクス
  - 実行時間
  - スループット
  - エラー率
- イベントストア統計
  - イベント数
  - ストレージサイズ
  - 書き込み/読み取り速度
- カスタムメトリクスプロバイダー
  - Prometheus対応
  - CloudWatch統合

### 16.2 分散トレーシング
**優先度: 高**
- OpenTelemetry統合
  - 自動スパン生成
  - コンテキスト伝播
- 相関ID伝播
  - リクエスト追跡
  - エラー相関
- トレース可視化
  - Jaeger/Zipkin対応

### 16.3 ヘルスチェック
**優先度: 高**
- ストレージプロバイダーヘルス
- システム診断
  - メモリ使用率
  - CPU使用率
  - ディスクI/O
- パフォーマンス劣化検知
- アラートフック
  - Webhook
  - メール通知
  - Slack統合

## Phase 17: マルチテナンシー＆セキュリティ（第17週）
**エンタープライズ要件への対応 - 優先度: 高**

### 17.1 マルチテナンシーサポート
**優先度: 高**
- テナント分離戦略
  - 論理分離
  - 物理分離オプション
- テナント別暗号化
  - キー管理
  - ローテーション
- テナント固有設定
  - カスタムバリデーション
  - 機能フラグ
- クロステナントクエリ防止

### 17.2 セキュリティ機能
**優先度: 高**
- イベント暗号化（保存時）
  - AES-256暗号化
  - 暗号化キー管理
- フィールドレベル暗号化
  - PII保護
  - 選択的暗号化
- 監査ログ
  - 全操作の記録
  - 改ざん防止
- アクセス制御統合
  - RBAC/ABAC
  - OAuth2/OIDC

### 17.3 コンプライアンスツール
**優先度: 中**
- GDPRコンプライアンスヘルパー
  - データエクスポート
  - 匿名化機能
- データ保持ポリシー
  - 自動削除
  - アーカイブ
- 忘れられる権利のサポート
  - イベント削除
  - 参照削除
- PII検出
  - 自動スキャン
  - レポート生成

## Phase 18: 統合＆メッセージング（第18週）
**外部システムとの連携 - 優先度: 中**

### 18.1 メッセージバス統合
**優先度: 高**
- Kafka/RabbitMQへのイベント発行
  - 自動発行設定
  - バッチ処理
- 外部イベント取り込み
  - イベント変換
  - 検証機能
- 配信保証パターン
  - At-least-once
  - Exactly-once
- デッドレターキュー処理

### 18.2 REST/GraphQL API
**優先度: 中**
- 自動生成APIエンドポイント
  - コマンドAPI
  - クエリAPI
- OpenAPIドキュメント生成
- GraphQLスキーマ生成
  - 型定義自動生成
  - リゾルバー生成
- WebSocketサブスクリプション
  - リアルタイムイベント配信
  - 接続管理

### 18.3 外部システムコネクター
**優先度: 低**
- Webhookディスパッチャー
  - 設定可能なエンドポイント
  - リトライロジック
- サードパーティサービス統合
  - 認証プロバイダー
  - 通知サービス
- イベント変換アダプター
  - 外部フォーマット対応
  - 双方向変換

## Phase 19: パフォーマンス＆スケーラビリティ（第19週）
**大規模システムへの対応 - 優先度: 高**

### 19.1 パフォーマンス最適化
**優先度: 高**
- イベントバッチ処理
  - バルク書き込み
  - バルク読み取り
- 並列アグリゲートローディング
  - 非同期I/O活用
  - 接続プール最適化
- メモリプール管理
  - オブジェクトプーリング
  - GC圧力軽減
- クエリ結果キャッシング
  - TTL管理
  - 無効化戦略

### 19.2 スケーラビリティ機能
**優先度: 高**
- 水平パーティショニング戦略
  - シャーディング
  - 一貫性ハッシング
- 読み取りモデル分離
  - CQRS最適化
  - 読み取りレプリカ
- イベントストリームシャーディング
  - パーティション戦略
  - リバランシング
- ロードバランシングサポート

### 19.3 キャッシング戦略
**優先度: 中**
- マルチレベルキャッシング
  - L1: メモリ内
  - L2: Redis/Memcached
- キャッシュ無効化パターン
  - イベントベース
  - TTLベース
- 分散キャッシュサポート
  - Redis Cluster
  - Hazelcast
- キャッシュウォーミング戦略

## Phase 20: 本番環境対応＆仕上げ（第20週）
**プロダクションレディネス - 優先度: 最高**

### 20.1 本番環境ツール
**優先度: 最高**
- ゼロダウンタイムデプロイメント
  - ローリングアップデート
  - カナリアリリース
- Blue-Greenデプロイメントパターン
- フィーチャーフラグ統合
  - LaunchDarkly対応
  - 段階的ロールアウト
- グレースフルシャットダウン
  - 実行中タスクの完了待機
  - 接続ドレイン

### 20.2 ドキュメント＆サンプル
**優先度: 高**
- 包括的APIドキュメント
  - TypeDoc生成
  - インタラクティブ例
- ベストプラクティスガイド
  - パターンカタログ
  - アンチパターン
- 実世界のサンプルアプリケーション
  - eコマース
  - 銀行システム
  - IoTデータ処理
- 移行ガイド
  - 他フレームワークからの移行
  - バージョンアップグレード

### 20.3 コミュニティ＆エコシステム
**優先度: 中**
- プラグインアーキテクチャ
  - 拡張ポイント定義
  - プラグインAPI
- 拡張ポイントドキュメント
- コミュニティテンプレートリポジトリ
- ベンチマークスイート
  - パフォーマンス比較
  - 最適化ガイド

## 実装優先度マトリックス

### 即座に着手（Phase 11-13）
- 永続ストレージ（PostgreSQL、CosmosDB）
- スナップショット管理
- イベントバージョニング

### 短期（Phase 14-16）
- テストフレームワーク
- プロセスマネージャー
- モニタリング

### 中期（Phase 17-19）
- マルチテナンシー
- 統合パターン
- パフォーマンス最適化

### 最終仕上げ（Phase 20）
- 本番環境ツール
- ドキュメント
- コミュニティ構築

## 推奨開始点

**Phase 1.1 Date Producer**から開始することを強く推奨します。
- 最もシンプルで理解しやすい
- 他の全てのコンポーネントが依存する基盤
- TDDサイクルの練習に最適
- モック作成が容易

Phase 1-12は完了済みのため、**Phase 13: イベントバージョニング＆スキーマ進化**から継続することを推奨します。

## 完了済みフェーズ
- Phase 1-10: 基盤実装完了
- Phase 11: 永続ストレージ実装（PostgreSQL、CosmosDB）完了
- Phase 12: スナップショット管理（Dapr Actors統合）完了