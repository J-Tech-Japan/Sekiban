# コアコンセプト - Sekiban イベントソーシング

> **ナビゲーション**
> - [コア概念](01_core_concepts.md) (現在位置)
> - [はじめに](02_getting_started.md)
> - [アグリゲート、プロジェクター、コマンド、イベント](03_aggregate_command_events.md)
> - [複数アグリゲートプロジェクター](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [ワークフロー](06_workflow.md)
> - [JSONとOrleansシリアライゼーション](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントAPI (Blazor)](09_client_api_blazor.md)
> - [Orleans設定](10_orleans_setup.md)
> - [Dapr設定](11_dapr_setup.md)
> - [ユニットテスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイメントガイド](16_deployment.md)

## コアコンセプト

イベントソーシング：すべての状態変更を不変のイベントとして保存します。現在の状態はイベントを再生することで導き出されます。

## 命名規則

- コマンド：命令形の動詞（Create、Update、Delete）
- イベント：過去形の動詞（Created、Updated、Deleted）
- 集約：ドメインエンティティを表す名詞
- プロジェクター：投影する集約にちなんで命名

## イベントソーシングの主要な原則

イベントソーシングは次のようなアーキテクチャパターンです：

1. **イベントとしての状態変更**：アプリケーションの状態に対するすべての変更はイベントのシーケンスとして保存されます
2. **不変のイベントログ**：記録されたイベントは変更または削除されることはありません
3. **プロジェクションによる現在の状態**：現在の状態はイベントを順番に再生することで計算されます
4. **完全な監査証跡**：イベントログはすべての変更の完全な履歴を提供します

## Sekibanを使用する利点

1. **完全な履歴**：ドメインの変更についての完全な監査証跡
2. **タイムトラベル**：任意の時点での状態を再構築する機能
3. **ドメイン重視**：明確なドメインモデルによる関心の分離の向上
4. **スケーラビリティ**：読み取りと書き込み操作を個別にスケールできる
5. **イベント駆動アーキテクチャ**：イベント駆動システムとの自然な統合

## コアコンポーネント

- **集約（Aggregate）**：状態とビジネスルールをカプセル化するドメインエンティティ
- **コマンド（Command）**：システム状態を変更するためのユーザーの意図を表す
- **イベント（Event）**：発生した状態変更の不変の記録
- **プロジェクター（Projector）**：イベントを集約に適用して現在の状態を構築する
- **クエリ（Query）**：現在の状態に基づいてシステムからデータを取得する

## PartitionKeys：イベントストリーム管理

PartitionKeysはSekibanの基本的な概念で、物理的なイベントストリームを管理します。各イベントストリームは次の3つの要素を持つPartitionKeysオブジェクトによって一意に識別されます：

```csharp
using Sekiban.Pure.Documents;

public record PartitionKeys(
    Guid AggregateId,
    string Group,
    string RootPartitionKey);
```

1. **AggregateId (Guid)**：特定の集約インスタンスの一意の識別子。これは通常、システムによって生成されるか、既存の集約のアドレス指定時に提供されるバージョン7のUUIDです。

2. **AggregateGroup (string)**：通常はプロジェクター名と同じです。同じグループを持つ集約はAggregateListProjectorを使用して簡単にクエリできます。これにより、関連する集約の論理的なグループ化が可能になります。

3. **RootPartitionKey (string)**：テナント分離とデータ分割に使用されます。デフォルトは"default"ですが、異なるテナント、環境、または他の論理的な分割間でデータを分離するために設定できます。

**PartitionKeysの使用例：**

```csharp
using Sekiban.Pure.Documents;
using Sekiban.Pure.Projectors;

// 新しい集約の場合（新しいAggregateIdを生成）
PartitionKeys keys = PartitionKeys.Generate<UserProjector>();

// 既存の集約の場合
PartitionKeys keys = PartitionKeys.Existing<UserProjector>(existingId);

// カスタムテナント/パーティションを使用
PartitionKeys keys = PartitionKeys.Generate<UserProjector>("tenant123");
PartitionKeys keys = PartitionKeys.Existing<UserProjector>(existingId, "tenant123");
```

**PartitionKeysの利点：**

1. **物理的なストリーム管理**：イベントの保存と取得方法を制御します
2. **グループ化**：AggregateGroupにより関連する集約を簡単にクエリできます
3. **マルチテナンシー**：RootPartitionKeyはマルチテナントアプリケーションのデータ分離を容易にします
4. **スケーラビリティ**：データの効率的なシャーディングとパーティショニングが可能になります

## イベントソーシングと従来のCRUDの比較

| 側面             | イベントソーシング                                  | 従来のCRUD                           |
|-------------------|------------------------------------------------|-------------------------------------------|
| データストレージ      | 不変のイベントログ                            | 変更可能な状態レコード                      |
| 状態管理  | イベントシーケンスから導出                    | 現在の状態の直接操作       |
| 履歴           | 完全な履歴が保持される                      | 限られた履歴または別のログが必要  |
| 並行処理       | イベントシーケンスによる自然な競合解決 | ロックまたは楽観的並行性制御が必要 |
| 監査証跡       | 組み込み                                       | 追加実装が必要         |
| 時間的クエリ  | 過去の状態に対するネイティブサポート            | 困難、追加設計が必要      |
| ドメインモデリング   | 振る舞いが豊富なドメインモデルを奨励         | 貧血ドメインモデルに陥りやすい        |

## Sekibanアーキテクチャ

Sekibanは次のようなクリーンでモダンなイベントソーシングアプローチを実装しています：

1. **Orleansとの統合**：高度にスケーラブルな分散ランタイム
2. **JSONシリアライゼーション**：柔軟で人間が読めるイベントストレージ
3. **強力な型付け**：型安全なコマンド、イベント、集約
4. **最小限のインフラストラクチャ**：最小限の設定でのシンプルなセットアップ
5. **ソース生成**：ドメインタイプの自動登録

## 集約設計の原則

### 集約の責任

Sekibanにおける集約は、一貫性境界としての役割を果たし、ビジネスルールをカプセル化します：

1. **ビジネスルールの実施**：集約はイベントが生成される前にビジネス不変条件を検証します
2. **一貫性境界**：各集約は他の集約に依存することなく、独自の一貫性を維持します
3. **イベント生成**：集約はコマンドを処理し、ビジネス操作の結果としてイベントを生成します
4. **状態管理**：現在の集約状態は、イベントを順番に適用することで導出されます

### パフォーマンス考慮事項

- **イベント数の制限**：1つの集約ストリームは**10,000イベント以下**のライフサイクルとなるよう設計してください
- **ストリーム分割**：長期間存続する集約は、適切な境界でストリームを分割することを検討してください
- **スナップショット機能**：大量のイベントを持つ集約にはスナップショット機能の利用を検討してください

### 集約の認証規則

- **認証/認可ロジックなし**：集約とそのコマンドハンドラーには認証や認可ロジックを含めてはいけません
- **純粋なビジネスロジック**：ドメインルールとビジネス不変条件のみに集中します
- **関心の分離**：認可はワークフローレベルで処理されます

### コマンド設計の原則

- **内部一貫性のみ**：コマンドは集約内での一貫性のみを考慮し、外部集約や外部システムに依存してはいけません
- **外部情報の入力**：必要な外部情報は全てコマンドパラメータとして入力してください
- **取得処理の禁止**：コマンド内で外部データの取得処理（データベースクエリ、API呼び出しなど）を実行してはいけません
- **決定論的動作**：同じ入力に対して常に同じ結果を返すよう設計してください

```csharp
/// <summary>
/// ユーザー集約はユーザー関連のビジネス操作を処理します
/// </summary>
public record UserProjector : IProjector<UserProjector>
{
    public string Email { get; init; } = string.Empty;
    public TenantId TenantId { get; init; } = new(Guid.Empty);
    public bool IsActive { get; init; } = true;
    
    public UserProjector ApplyEvent(UserCreated ev) => this with 
    { 
        Email = ev.Email, 
        TenantId = ev.TenantId,
        IsActive = true 
    };
}

/// <summary>
/// 新しいユーザーを作成するコマンド - 認可ロジックなし
/// 外部情報は全てパラメータとして受け取る
/// </summary>
public record CreateUserCommand(
    string Email,
    TenantId TenantId,
    DateTime? CreatedAt = null
) : ICommand<UserProjector>
{
    public ResultBox<EventOrNone> Handle(UserProjector projector, ICommandContext context)
    {
        // 純粋なビジネスロジックのみ - 外部取得処理なし
        if (string.IsNullOrEmpty(Email))
            return ResultBox.FromError("Email is required");
            
        // 集約内一貫性チェックのみ
        if (projector.Email == Email)
            return ResultBox.FromError("User with this email already exists");
            
        return EventOrNone.Event(new UserCreated(Email, TenantId, CreatedAt ?? DateTime.UtcNow));
    }
    
    public PartitionKeys SpecifyPartitionKeys(CreateUserCommand command) => 
        PartitionKeys.Generate<UserProjector>();
}
```

### マルチテナント集約設計

すべての集約（Tenant自体を除く）にはTenantIdを含める必要があります：

```csharp
/// <summary>
/// マルチテナンシーのために必要なTenantIdを持つTimeSheet集約
/// </summary>
public record TimeSheetProjector : IProjector<TimeSheetProjector>
{
    public TenantId TenantId { get; init; } = new(Guid.Empty);
    public UserId UserId { get; init; } = new(Guid.Empty);
    public DateTime Date { get; init; }
    public List<TimeEntry> Entries { get; init; } = new();
    
    // イベントハンドラーはTenantIdを維持します
    public TimeSheetProjector ApplyEvent(TimeSheetCreated ev) => this with 
    { 
        TenantId = ev.TenantId,
        UserId = ev.UserId,
        Date = ev.Date 
    };
}
```

## ワークフロー設計の原則

### ワークフローの責任

ワークフローは複数の集約にまたがるビジネスプロセスを調整し、認可を処理します：

1. **複数集約間の操作**：複数の集約にまたがる操作を調整します
2. **認証と認可**：RBACベースのアクセス制御を実装します
3. **ビジネスプロセスの調整**：複雑なビジネスワークフローを処理します
4. **外部システム統合**：外部サービスとの相互作用を管理します

### ワークフローでのRBAC実装

```csharp
/// <summary>
/// RBAC認可を伴うユーザー管理用ワークフロー
/// </summary>
public record CreateUserWorkflow : IWorkflow<CreateUserWorkflow>
{
    public async Task<ResultBox<WorkflowResult>> ExecuteAsync(
        CreateUserWorkflowInput input,
        UserId executingUser,
        IWorkflowContext context)
    {
        // 認可チェック - 管理者のみがユーザーを作成できます
        var executingUserProjector = await context.GetAggregateAsync<UserProjector>(
            PartitionKeys.Existing<UserProjector>(executingUser.Value));
            
        if (!executingUserProjector.IsAdministrator)
            return ResultBox.FromError("Unauthorized: Only administrators can create users");
        
        // テナント検証 - 実行ユーザーが同じテナントに属することを確認
        if (executingUserProjector.TenantId != input.TenantId)
            return ResultBox.FromError("Unauthorized: Cross-tenant operations not allowed");
        
        // ビジネス操作を実行
        var command = new CreateUserCommand(input.Email, input.TenantId);
        var result = await context.ExecuteCommandAsync(command);
        
        return WorkflowResult.FromCommandResult(result);
    }
}

/// <summary>
/// ワークフロー入力 - セキュリティのためExecutingUserから分離
/// </summary>
public record CreateUserWorkflowInput(
    string Email,
    TenantId TenantId
);
```

### 認可パターン

- **ExecutingUserの分離**：API操作を防ぐため、ExecutingUserは常にワークフロー入力から分離して渡します
- **テナント検証**：実行ユーザーのテナントが対象集約のテナントと一致することを確認します
- **ロールベースアクセス**：ワークフロー内でロールチェックを実装します
- **エラーハンドリング**：認証されていないアクセスに対して適切なエラーメッセージを返します

## イベントストリーム設計

### ストリーム識別戦略

Sekibanのイベントストリームは、3つの重要な要素を持つPartitionKeysによって識別されます：

1. **AggregateId**：特定の集約インスタンスの一意の識別子
2. **AggregateGroup**：論理的なグループ化（通常はプロジェクター名）
3. **RootPartitionKey**：テナント/環境の分離

### PartitionKeys実装パターン

#### 新しい集約の場合

```csharp
/// <summary>
/// 作成操作用の新しい集約IDを生成
/// </summary>
public PartitionKeys SpecifyPartitionKeys(CreateTenantCommand command) => 
    PartitionKeys.Generate<TenantProjector>();

/// <summary>
/// テナント分離を伴う生成
/// </summary>
public PartitionKeys SpecifyPartitionKeys(CreateUserCommand command) => 
    PartitionKeys.Generate<UserProjector>(command.TenantId.Value.ToString());
```

#### 既存の集約の場合

```csharp
/// <summary>
/// IDによる既存集約の参照
/// </summary>
public PartitionKeys SpecifyPartitionKeys(UpdateUserCommand command) => 
    PartitionKeys.Existing<UserProjector>(command.UserId.Value);

/// <summary>
/// テナント分離を伴う既存集約の参照
/// </summary>
public PartitionKeys SpecifyPartitionKeys(UpdateUserCommand command) => 
    PartitionKeys.Existing<UserProjector>(
        command.UserId.Value, 
        command.TenantId.Value.ToString());
```

### マルチテナントストリーム設計

マルチテナントアプリケーションでは、データ分離にRootPartitionKeyを使用します：

```csharp
/// <summary>
/// テナント識別用のValue Object
/// </summary>
[GenerateSerializer]
public record TenantId([property:Required] Guid Value);

/// <summary>
/// 適切なPartitionKeysを持つテナント対応コマンド
/// </summary>
public record AddUserToTenantCommand(
    UserId UserId,
    TenantId TenantId,
    string Email
) : ICommand<UserProjector>
{
    public PartitionKeys SpecifyPartitionKeys(AddUserToTenantCommand command) => 
        PartitionKeys.Existing<UserProjector>(
            command.UserId.Value, 
            command.TenantId.Value.ToString());
}
```

### ストリーム設計の利点

1. **テナント分離**：RootPartitionKeyは完全なデータ分離を保証します
2. **スケーラブルなクエリ**：AggregateGroupは効率的な集約リストクエリを可能にします
3. **パフォーマンス最適化**：適切なパーティショニングはクエリパフォーマンスを向上させます
4. **データガバナンス**：データアクセスと管理の明確な境界

## Value Object実装

### 検証ルール

コンストラクタ検証の代わりにData Annotationを使用して検証を行います：

```csharp
/// <summary>
/// 検証付きのテナントコードvalue object
/// </summary>
[GenerateSerializer]
public record TenantCode([property:Required, property:StringLength(50, MinimumLength = 1)] string Value);

/// <summary>
/// ユーザーIDのvalue object
/// </summary>
[GenerateSerializer]
public record UserId([property:Required] Guid Value);
```

### DateTime処理

テスト容易性のためコマンドプロパティとしてオプションのDateTimeを受け取ります：

```csharp
/// <summary>
/// テストの柔軟性のためにオプションのDateTimeを持つコマンド
/// </summary>
public record CreateTenantCommand(
    TenantCode Code, 
    string Name,
    DateTime? CreatedAt = null
) : ICommand<TenantProjector>
{
    public ResultBox<EventOrNone> Handle(TenantProjector projector, ICommandContext context)
    {
        return EventOrNone.Event(new TenantCreated(
            Code, 
            Name, 
            CreatedAt ?? DateTime.UtcNow));
    }
}
```

この設計により以下が保証されます：
- **テスト容易性**：テストで正確なタイムスタンプを指定できます
- **ワークフロー制御**：ワークフローは操作間でタイムスタンプを調整できます
- **デフォルト動作**：指定されていない場合の現在時刻への自然なフォールバック

## アーキテクチャのベストプラクティス

### 集約ストリーム設計

- **イベント数制限**：1つの集約ストリームは10,000イベント以下のライフサイクルで設計する
- **適切な境界設定**：長期間存続する集約は適切な境界でストリームを分割する
- **スナップショット活用**：大量のイベントを持つ集約にはスナップショット機能を活用する

### コマンドとワークフローの責任分離

- **コマンド**：集約内の一貫性とビジネスルールの実施のみを担当
- **ワークフロー**：外部データ取得、複数集約間の調整、認証・認可を担当