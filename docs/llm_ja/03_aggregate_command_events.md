# 集約ペイロード、プロジェクター、コマンド、イベント - Sekiban イベントソーシング

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [集約、プロジェクター、コマンド、イベント](03_aggregate_command_events.md) (現在のページ)
> - [複数集約プロジェクター](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [ワークフロー](06_workflow.md)
> - [JSONとOrleansのシリアライゼーション](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントAPI (Blazor)](09_client_api_blazor.md)
> - [Orleansセットアップ](10_orleans_setup.md)
> - [ユニットテスト](11_unit_testing.md)
> - [一般的な問題と解決策](12_common_issues.md)
> - [ResultBox](13_result_box.md)

## 1. 集約ペイロード（ドメインエンティティ）

集約は状態とビジネスルールをカプセル化するドメインエンティティです。Sekibanでは、集約は不変のレコードとして実装されます：

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Aggregates;

[GenerateSerializer]
public record YourAggregate(...properties...) : IAggregatePayload
{
    // ドメインロジックメソッド
}
```

**必須**:
- `IAggregatePayload` インターフェースを実装
- 不変性のためにC#のrecordを使用
- Orleans用の `[GenerateSerializer]` 属性を追加

### 例：ユーザー集約

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Aggregates;

[GenerateSerializer]
public record User(string Name, string Email, bool IsConfirmed = false) : IAggregatePayload
{
    // ドメインロジックメソッド
    public User WithConfirmation() => this with { IsConfirmed = true };
    
    public User UpdateEmail(string newEmail) => this with { Email = newEmail };
}
```

## 2. コマンド（ユーザーの意図）

コマンドはシステム状態を変更するためのユーザーの意図を表します。これらはハンドラーを持つレコードとして実装されます：

### コマンド設計の原則

- **内部一貫性のみ**：コマンドは集約境界内のビジネスルールのみを検証すべきです
- **外部依存関係なし**：コマンドは外部集約やシステムに依存してはいけません
- **パラメータベースの入力**：必要な外部情報は全てコマンドパラメータとして提供する必要があります
- **取得処理の禁止**：コマンドはデータベースクエリ、API呼び出し、その他の外部データ取得を実行してはいけません
- **決定論的動作**：同じ入力は常に同じ結果を生成すべきです

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.ResultBoxes;

[GenerateSerializer]
public record YourCommand(...parameters...) 
    : ICommandWithHandler<YourCommand, YourAggregateProjector>
{
    // 必須メソッド
    // 新しい集約の場合:
    public PartitionKeys SpecifyPartitionKeys(YourCommand command) => 
        PartitionKeys.Generate<YourAggregateProjector>();
        
    // 既存の集約の場合:
    // public PartitionKeys SpecifyPartitionKeys(YourCommand command) => 
    //    PartitionKeys.Existing<YourAggregateProjector>(command.AggregateId);

    public ResultBox<EventOrNone> Handle(YourCommand command, ICommandContext<IAggregatePayload> context)
    {
        // 純粋なビジネスロジックのみ - 外部データ取得なし
        // コマンドパラメータと現在の集約状態のみを使用して検証
        return EventOrNone.Event(new YourEvent(...parameters...));
    }
}
```
```

**必須**:
- `ICommandWithHandler<TCommand, TProjector>` インターフェースを実装、または状態ベースの制約を強制する必要がある場合は `ICommandWithHandler<TCommand, TProjector, TPayloadType>` を実装
- `SpecifyPartitionKeys` メソッドの実装:
  - 新しい集約の場合: `PartitionKeys.Generate<YourProjector>()`
  - 既存の集約の場合: `PartitionKeys.Existing<YourProjector>(aggregateId)`
- イベントを返す `Handle` メソッドの実装
- `[GenerateSerializer]` 属性の追加

### 例：ユーザー作成コマンド

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.ResultBoxes;

[GenerateSerializer]
public record CreateUser(string Name, string Email) 
    : ICommandWithHandler<CreateUser, UserProjector>
{
    public PartitionKeys SpecifyPartitionKeys(CreateUser command) => 
        PartitionKeys.Generate<UserProjector>();
        
    public ResultBox<EventOrNone> Handle(CreateUser command, ICommandContext<IAggregatePayload> context)
        => EventOrNone.Event(new UserCreated(command.Name, command.Email));
}
```

### 状態制約のための第3ジェネリックパラメータの使用

型レベルで状態ベースの制約を強制するために、第3のジェネリックパラメータを指定できます：

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.ResultBoxes;
using System;

[GenerateSerializer]
public record RevokeUser(Guid UserId) 
    : ICommandWithHandler<RevokeUser, UserProjector, ConfirmedUser>
{
    public PartitionKeys SpecifyPartitionKeys(RevokeUser command) => 
        PartitionKeys<UserProjector>.Existing(UserId);
    
    public ResultBox<EventOrNone> Handle(RevokeUser command, ICommandContext<ConfirmedUser> context) =>
        context
            .GetAggregate()
            .Conveyor(_ => EventOrNone.Event(new UserUnconfirmed()));
}
```

**利点**:
- 第3のジェネリックパラメータ（例の `ConfirmedUser`）は、このコマンドが現在の集約ペイロードがその特定のタイプである場合にのみ実行できることを指定します
- コマンドコンテキストは `ICommandContext<IAggregatePayload>` の代わりに `ICommandContext<ConfirmedUser>` として強く型付けされます
- 状態依存操作のためのコンパイル時の安全性を提供します
- 実行者はコマンドを実行する前に、現在のペイロードタイプが指定された型と一致するかどうかを自動的にチェックします
- エンティティの異なる状態を表現するために集約ペイロードタイプを使用する場合（ステートマシンパターン）に特に有用です

### コマンドでの集約ペイロードへのアクセス

コマンドハンドラーでの集約ペイロードへのアクセスには、2つまたは3つのジェネリックパラメータバージョンを使用するかどうかに応じて、2つの方法があります：

1. **型制約を使用する場合（3つのジェネリックパラメータ）**:
   ```csharp
   using Sekiban.Pure.Aggregates;
   using Sekiban.Pure.Command.Handlers;
   using Sekiban.Pure.Events;
   using Sekiban.Pure.ResultBoxes;

   // ICommandWithHandler<TCommand, TProjector, TAggregatePayload>を使用
   public ResultBox<EventOrNone> Handle(YourCommand command, ICommandContext<ConfirmedUser> context)
   {
       // 強く型付けされた集約とペイロードへの直接アクセス
       var aggregate = context.GetAggregate();
       var payload = aggregate.Payload; // すでにConfirmedUserとして型付けされています
       
       // ペイロードプロパティを直接使用
       var userName = payload.Name;
       
       return EventOrNone.Event(new YourEvent(...));
   }
   ```

2. **型制約なし（2つのジェネリックパラメータ）**:
   ```csharp
   using Sekiban.Pure.Aggregates;
   using Sekiban.Pure.Command.Handlers;
   using Sekiban.Pure.Events;
   using Sekiban.Pure.ResultBoxes;

   // ICommandWithHandler<TCommand, TProjector>を使用
   public ResultBox<EventOrNone> Handle(YourCommand command, ICommandContext<IAggregatePayload> context)
   {
       // ペイロードを期待される型にキャストする必要があります
       if (context.GetAggregate().GetPayload() is ConfirmedUser payload)
       {
           // 型付きペイロードが使えるようになりました
           var userName = payload.Name;
           
           return EventOrNone.Event(new YourEvent(...));
       }
       
       // ペイロードが期待される型でない場合を処理
       return new SomeException("ConfirmedUser状態が必要です");
   }
   ```

3パラメータバージョンは、集約があるべき正確な状態を知っている場合に好ましく、コンパイル時の安全性とよりクリーンなコードを提供します。

### コマンドから複数のイベントを生成する

コマンドが複数のイベントを生成する必要がある場合は、コマンドコンテキストの `AppendEvent` メソッドを使用できます：

```csharp
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;
using Sekiban.Pure.ResultBoxes;

public ResultBox<EventOrNone> Handle(ComplexCommand command, ICommandContext<TAggregatePayload> context)
{
    // まず、イベントを一つずつ追加
    context.AppendEvent(new FirstEventHappened(command.SomeData));
    context.AppendEvent(new SecondEventHappened(command.OtherData));
    
    // すべてのイベントが追加されたことを示すためにEventOrNone.Noneを返します
    return EventOrNone.None;
    
    // あるいは、最後のイベントを返すことも可能です
    // return EventOrNone.Event(new FinalEventHappened(command.FinalData));
}
```

**重要なポイント**:
- イベントストリームにイベントを追加するには `context.AppendEvent(eventPayload)` を使用します
- 複数のイベントを順番に追加できます
- すべてのイベントが `AppendEvent` を使用して追加された場合は `EventOrNone.None` を返します
- または、そのアプローチを好む場合は `EventOrNone.Event` を使用して最後のイベントを返します
- 追加されたすべてのイベントは、追加された順序で集約に適用されます

## 3. イベント（発生した事実）

イベントは2つの部分で構成されています：

1. **イベントメタデータ**（Sekibanが処理）:
   ```csharp
   // これらはシステムによって管理されます
   PartitionKeys partitionKeys;
   DateTime timestamp;
   Guid id;
   int version;
   // その他のシステムメタデータ
   ```

2. **イベントペイロード**（開発者が定義）:
   ```csharp
   using Orleans.Serialization.Attributes;
   using Sekiban.Pure.Events;

   [GenerateSerializer]
   public record YourEvent(...parameters...) : IEventPayload;
   ```

**必須**:
- ドメイン固有のデータのみのための `IEventPayload` インターフェースを実装
- 過去形の命名（Created、Updated、Deleted）を使用
- `[GenerateSerializer]` 属性を追加
- ドメイン状態を再構築するために必要なすべてのデータを含める

### 例：ユーザーイベント

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Events;

[GenerateSerializer]
public record UserCreated(string Name, string Email) : IEventPayload;

[GenerateSerializer]
public record UserConfirmed : IEventPayload;

[GenerateSerializer]
public record UserUnconfirmed : IEventPayload;

[GenerateSerializer]
public record EmailChanged(string NewEmail) : IEventPayload;
```

## PartitionKeys構造

```csharp
using Sekiban.Pure.Documents;

public class PartitionKeys
{
    string RootPartitionKey;  // マルチテナンシー用のオプショナルなテナントキー
    string AggregateGroup;    // 通常はプロジェクター名
    Guid AggregateId;        // 一意の識別子
}
```

**コマンドでの使用法**:
```csharp
using Sekiban.Pure.Documents;

// 新しい集約の場合:
public PartitionKeys SpecifyPartitionKeys(YourCommand command) => 
    PartitionKeys.Generate<YourProjector>();

// 既存の集約の場合:
public PartitionKeys SpecifyPartitionKeys(YourCommand command) => 
    PartitionKeys.Existing<YourProjector>(command.AggregateId);
```

## 4. プロジェクター（状態ビルダー）

プロジェクターは、イベントを集約に適用することで現在の状態を構築します：

```csharp
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;

// プロジェクターにおける状態遷移の例
public class UserProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
        => (payload, ev.GetPayload()) switch
        {
            // 各ケースは状態を表現するために異なるペイロードタイプを返すことができます
            (EmptyAggregatePayload, UserCreated e) => new UnconfirmedUser(e.Name, e.Email),
            (UnconfirmedUser user, UserConfirmed _) => new ConfirmedUser(user.Name, user.Email),
            (ConfirmedUser user, UserUnconfirmed _) => new UnconfirmedUser(user.Name, user.Email),
            _ => payload
        };
}

// 異なる状態は異なる操作を可能にします
public record UnconfirmedUser(string Name, string Email) : IAggregatePayload;
public record ConfirmedUser(string Name, string Email) : IAggregatePayload;
```

**必須**:
- `IAggregateProjector` インターフェースを実装
- 状態遷移を管理するためにパターンマッチングを使用
- ビジネスルールを強制するために異なるペイロードタイプを返す
- `EmptyAggregatePayload` からの初期状態作成を処理
- 状態変更における不変性を維持

### 例：Todoアイテムプロジェクター

```csharp
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;

public class TodoItemProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
        => (payload, ev.GetPayload()) switch
        {
            // 初期状態の作成
            (EmptyAggregatePayload, TodoItemCreated e) => new TodoItem(e.Title),
            
            // 状態更新
            (TodoItem item, TodoItemCompleted _) => item with { IsCompleted = true },
            (TodoItem item, TodoItemDescriptionChanged e) => item with { Description = e.Description },
            (TodoItem item, TodoItemDueDateSet e) => item with { DueDate = e.DueDate },
            
            // デフォルトケース：変更されていないペイロードを返す
            _ => payload
        };
}
```

## 単一集約に対する複数のプロジェクター

Sekibanの強力な機能の1つは、同じイベントストリームに対して複数のプロジェクターを使用できることです。PartitionKeysが揃っていれば、異なるプロジェクターを使用して同じイベントの異なるビューを作成できます。

### 複数のプロジェクションのためのLoadAggregateAsyncの使用

`SekibanOrleansExecutor`の`LoadAggregateAsync`メソッドは、同じストリーム構造を共有する任意のプロジェクターを使用して集約をロードできるようにします：

```csharp
using Sekiban.Pure.Executors;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.ResultBoxes;
using System.Threading.Tasks;
using System;

// まず、主要プロジェクターでロード
var partitionKeys = PartitionKeys.Existing<UserProjector>(userId);
var result = await sekibanExecutor.LoadAggregateAsync<UserProjector>(partitionKeys);

// 次に、同じイベントを異なるプロジェクターでロード
var userActivityResult = await sekibanExecutor.LoadAggregateAsync<UserActivityProjector>(partitionKeys);

// 両方のプロジェクションは同じイベントを使用しますが、異なるビューを生成します
if (result.IsSuccess && userActivityResult.IsSuccess)
{
    var user = result.GetValue().GetPayload();  // 標準ユーザープロジェクション
    var activity = userActivityResult.GetValue().GetPayload();  // アクティビティ重視のプロジェクション
    
    // 両方のプロジェクションを使用して作業...
}
```

### 例：ユーザーデータの複数のプロジェクション

```csharp
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using System;
using System.Collections.Generic;

// 標準ユーザープロジェクター
public class UserProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
        => (payload, ev.GetPayload()) switch
        {
            (EmptyAggregatePayload, UserCreated e) => new User(e.Name, e.Email),
            (User user, EmailChanged e) => user with { Email = e.NewEmail },
            _ => payload
        };
}

// 同じイベントに対するアクティビティ重視のプロジェクター
public class UserActivityProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
    {
        var activityLog = payload is UserActivity activity 
            ? activity.ActivityLog 
            : new List<UserActivityEntry>();
            
        // イベントタイプに基づいて新しいアクティビティを追加
        switch (ev.GetPayload())
        {
            case UserCreated:
                activityLog.Add(new UserActivityEntry(ev.Timestamp, "ユーザー作成"));
                break;
            case EmailChanged e:
                activityLog.Add(new UserActivityEntry(ev.Timestamp, $"メールが{e.NewEmail}に変更されました"));
                break;
            case UserConfirmed:
                activityLog.Add(new UserActivityEntry(ev.Timestamp, "アカウント確認済み"));
                break;
        }
        
        return new UserActivity(activityLog);
    }
}

public record UserActivity(List<UserActivityEntry> ActivityLog) : IAggregatePayload;
public record UserActivityEntry(DateTime Timestamp, string Activity);
```

**複数のプロジェクターの利点**:

1. **異なる視点**: 同じイベントストリームを異なる角度から見る
2. **特殊化されたプロジェクション**: 同じデータのタスク固有のビューを作成
3. **関心の分離**: 主要な集約モデルをクリーンに保ちながら、特殊なビューを追加
4. **進化するモデル**: 既存のモデルを変更せずに新しいプロジェクターを追加