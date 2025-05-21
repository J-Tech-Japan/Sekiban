# はじめに - Sekiban イベントソーシング

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md) (現在のページ)
> - [集約、プロジェクター、コマンド、イベント](03_aggregate_command_events.md)
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

## インストールとセットアップ

```bash
# Sekibanテンプレートのインストール
dotnet new install Sekiban.Pure.Templates

# 新しいプロジェクトの作成
dotnet new sekiban-orleans-aspire -n MyProject
```

このテンプレートにはOrleans用のAspireホスト、クラスターストレージ、Grain永続ストレージ、キューストレージが含まれています。

## 重要な注意点

### 正しい名前空間
テンプレートは`Sekiban.Core.*`ではなく`Sekiban.Pure.*`名前空間階層を使用しています。常に次の名前空間を使用してください：

- `Sekiban.Pure.Aggregates` - 集約とペイロードインターフェース用
- `Sekiban.Pure.Events` - イベント用
- `Sekiban.Pure.Projectors` - プロジェクター用
- `Sekiban.Pure.Command.Handlers` - コマンドハンドラー用
- `Sekiban.Pure.Command.Executor` - コマンド実行コンテキスト用
- `Sekiban.Pure.Documents` - パーティションキー用
- `Sekiban.Pure.Query` - クエリ用
- `ResultBoxes` - 結果処理用

### プロジェクト構造
テンプレートは複数のプロジェクトを持つソリューションを作成します：
- `MyProject.Domain` - ドメインモデル、イベント、コマンド、クエリを含む
- `MyProject.ApiService` - コマンドとクエリ用のAPIエンドポイント
- `MyProject.Web` - Blazorを使用したWebフロントエンド
- `MyProject.AppHost` - サービスのオーケストレーション用Aspireホスト
- `MyProject.ServiceDefaults` - 共通サービス設定

### アプリケーションの実行
Aspireホストでアプリケーションを実行する場合は、次のコマンドを使用します：

```bash
dotnet run --project MyProject.AppHost
```

HTTPSプロファイルでAppHostを起動するには：

```bash
dotnet run --project MyProject.AppHost --launch-profile https
```

## ファイル構造

最新のテンプレートではより構造化されたフォルダ階層を使用しています：

```
YourProject.Domain/
├── Aggregates/                         // 集約関連フォルダ
│   └── YourEntity/                     // エンティティ固有のフォルダ
│       ├── Commands/                   // コマンド
│       │   ├── CreateYourEntityCommand.cs
│       │   ├── UpdateYourEntityCommand.cs
│       │   └── DeleteYourEntityCommand.cs
│       ├── Events/                     // イベント
│       │   ├── YourEntityCreated.cs
│       │   ├── YourEntityUpdated.cs
│       │   └── YourEntityDeleted.cs
│       ├── Payloads/                   // 集約ペイロード
│       │   └── YourEntity.cs
│       ├── Queries/                    // クエリ
│       │   └── YourEntityQuery.cs
│       └── YourEntityProjector.cs      // プロジェクター
├── Projections/                        // マルチプロジェクション
│   └── CustomProjection/
│       ├── YourCustomProjection.cs
│       └── YourCustomQuery.cs
├── ValueObjects/                       // 値オブジェクト
│   └── YourValueObject.cs
└── YourDomainEventsJsonContext.cs      // JSONコンテキスト
```

この構造はドメイン駆動設計の原則に従って、関連するコードをより論理的に整理するのに役立ちます。

## 最初のステップ

1. **ドメインモデルの定義**：ドメイン内の主要なエンティティを特定することから始める
2. **集約の作成**：各エンティティの集約ペイロードを実装する
3. **コマンドの定義**：ユーザーの意図を表すコマンドを作成する
4. **イベントの定義**：状態変更を記録するイベントを作成する
5. **プロジェクターの実装**：イベントから現在の状態を構築するプロジェクターを作成する
6. **クエリの追加**：データを取得するクエリタイプを追加する
7. **シリアライゼーションの設定**：ドメインタイプのJSONシリアライゼーションをセットアップする
8. **APIエンドポイントの追加**：コマンドとクエリのAPIエンドポイントを作成する

## 例：最小限のドメインの作成

最小限のTodoアプリケーションドメインを作成してみましょう：

1. TodoItem集約ペイロードの作成：
   ```csharp
   using Orleans.Serialization.Attributes;
   using Sekiban.Pure.Aggregates;

   [GenerateSerializer]
   public record TodoItem(string Title, bool IsCompleted = false) : IAggregatePayload;
   ```

2. TodoItemイベントの作成：
   ```csharp
   using Orleans.Serialization.Attributes;
   using Sekiban.Pure.Events;

   [GenerateSerializer]
   public record TodoItemCreated(string Title) : IEventPayload;
   
   [GenerateSerializer]
   public record TodoItemCompleted : IEventPayload;
   ```

3. TodoItemコマンドの作成：
   ```csharp
   using Orleans.Serialization.Attributes;
   using Sekiban.Pure.Aggregates;
   using Sekiban.Pure.Command.Handlers;
   using Sekiban.Pure.Documents;
   using Sekiban.Pure.Events;
   using Sekiban.Pure.ResultBoxes;

   [GenerateSerializer]
   public record CreateTodoItem(string Title) 
       : ICommandWithHandler<CreateTodoItem, TodoItemProjector>
   {
       public PartitionKeys SpecifyPartitionKeys(CreateTodoItem command) => 
           PartitionKeys.Generate<TodoItemProjector>();
           
       public ResultBox<EventOrNone> Handle(CreateTodoItem command, ICommandContext<IAggregatePayload> context)
           => EventOrNone.Event(new TodoItemCreated(command.Title));
   }
   ```

4. TodoItemプロジェクターの作成：
   ```csharp
   using Sekiban.Pure.Aggregates;
   using Sekiban.Pure.Events;
   using Sekiban.Pure.Projectors;

   public class TodoItemProjector : IAggregateProjector
   {
       public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
           => (payload, ev.GetPayload()) switch
           {
               (EmptyAggregatePayload, TodoItemCreated e) => new TodoItem(e.Title),
               (TodoItem item, TodoItemCompleted _) => item with { IsCompleted = true },
               _ => payload
           };
   }
   ```

次に、他のガイドに従ってクエリ、APIエンドポイントなどを実装してください。