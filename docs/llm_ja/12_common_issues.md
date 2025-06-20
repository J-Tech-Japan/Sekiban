# 一般的な問題と解決策 - Sekiban イベントソーシング

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [集約、プロジェクター、コマンド、イベント](03_aggregate_command_events.md)
> - [複数集約プロジェクター](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [ワークフロー](06_workflow.md)
> - [JSONとOrleansのシリアライゼーション](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントAPI (Blazor)](09_client_api_blazor.md)
> - [Orleansセットアップ](10_orleans_setup.md)
> - [ユニットテスト](11_unit_testing.md)
> - [一般的な問題と解決策](12_common_issues.md) (現在のページ)
> - [ResultBox](13_result_box.md)

## 一般的な問題と解決策

このセクションでは、Sekibanを使用する際に遭遇する可能性のある一般的な問題とその解決策について説明します。

## 1. 名前空間エラー

**問題**: 誤った名前空間によるコンパイラエラー。

**解決策**: `Sekiban.Core.*` ではなく、`Sekiban.Pure.*` 名前空間を使用していることを確認してください。最も一般的な名前空間は次のとおりです：

```csharp
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Query;
using Sekiban.Pure.ResultBoxes;
```

## 2. コマンドコンテキストエラー

**問題**: コマンドコンテキストから集約ペイロードに直接アクセスできない。

**解決策**: コマンドコンテキストは集約ペイロードを直接公開していません。パターンマッチングを使用してください：

```csharp
public ResultBox<EventOrNone> Handle(YourCommand command, ICommandContext<IAggregatePayload> context)
{
    if (context.GetAggregate().GetPayload() is YourAggregate aggregate)
    {
        // これで集約プロパティを使用できます
        var property = aggregate.Property;
        
        return EventOrNone.Event(new YourEvent(...));
    }
    
    return new SomeException("Expected YourAggregate");
}
```

または、`ICommandWithHandler`の3パラメーター版を使用してより強い型付けを行います：

```csharp
public record YourCommand(...) 
    : ICommandWithHandler<YourCommand, YourProjector, YourAggregateType>
{
    // これでコンテキストがICommandContext<YourAggregateType>に型付けされます
    public ResultBox<EventOrNone> Handle(YourCommand command, ICommandContext<YourAggregateType> context)
    {
        var aggregate = context.GetAggregate();
        // ペイロードはすでにYourAggregateTypeとして型付けされています
        var payload = aggregate.Payload;
        
        return EventOrNone.Event(new YourEvent(...));
    }
}
```

## 3. 時間の統一取得 - SekibanDateProducer

**問題**: Sekibanシステム内外で時間取得の方法が統一されていない。

**解決策**: `SekibanDateProducer`を使用してシステム全体で統一された時間を取得してください：

```csharp
// Sekibanと同じ方法で時間を取得
var currentTime = SekibanDateProducer.GetRegistered().UtcNow;
```

この方法により、Sekibanのイベントソーシングシステムと外部システムで同じ時間ソースを使用できます。テスト時には時間をモックすることも可能です。

## 4. シリアライゼーションの問題

**問題**: `System.NotSupportedException: Orleansシリアライゼーションには、型がシリアライズ可能である必要があります。`

**解決策**: コマンド、イベント、集約で使用されるすべての型に `[GenerateSerializer]` 属性があることを確認してください：

```csharp
[GenerateSerializer]
public record YourCommand(...);

[GenerateSerializer]
public record YourEvent(...);

[GenerateSerializer]
public record YourAggregate(...);
```

レコード型以外の場合は、フィールドとプロパティに `[Id]` 属性を使用します：

```csharp
public class ComplexType
{
    [Id(0)]
    public string PropertyA { get; set; } = null!;

    [Id(1)]
    public int PropertyB { get; set; }
}
```

## 5. ソース生成の問題

**問題**: `YourProjectDomainDomainTypes` クラスが見つからない。

**解決策**:

1. プロジェクトが正常にコンパイルされることを確認する
2. ドメインタイプが必要な属性を持って正しく定義されていることを確認する
3. 生成された型に対して正しい名前空間を使用する：`using YourProject.Domain.Generated;`
4. ソース生成をトリガーするためにソリューションを再ビルドする

## 6. クエリ結果の問題

**問題**: コマンドを実行した後、クエリが空または古い結果を返す。

**解決策**: 一貫性を確保するために `IWaitForSortableUniqueId` インターフェースを使用します：

```csharp
// コマンドを実行するとき、LastSortableUniqueIdを取得
var commandResult = await executor.CommandAsync(new YourCommand(...)).UnwrapBox();
var lastSortableId = commandResult.LastSortableUniqueId;

// それをクエリに渡す
var query = new YourQuery(...) { WaitForSortableUniqueId = lastSortableId };
var queryResult = await executor.QueryAsync(query).UnwrapBox();
```

## 7. コマンドからの複数イベント

**問題**: コマンドハンドラーから複数のイベントを返す必要がある。

**解決策**: `AppendEvent` メソッドを使用して `EventOrNone.None` を返します：

```csharp
public ResultBox<EventOrNone> Handle(ComplexCommand command, ICommandContext<TAggregatePayload> context)
{
    context.AppendEvent(new FirstEventHappened(command.SomeData));
    context.AppendEvent(new SecondEventHappened(command.OtherData));
    
    return EventOrNone.None;  // すべてのイベントが追加されたことを示す
}
```

## 8. Orleansクラスタリングの問題

**問題**: OrleansシロがクラスタにConnectできない。

**解決策**: クラスタリング設定を確認してください：

```csharp
// 開発用
siloBuilder.UseLocalhostClustering();

// Azure Storageを使用した本番用
siloBuilder.UseAzureStorageClustering(options =>
{
    options.ConfigureTableServiceClient(connectionString);
});

// Kubernetes用
siloBuilder.UseKubernetesHosting();
```

そして、接続文字列が正しく設定されていることを確認してください。

## 9. データベース設定

**問題**: アプリケーションがデータベースに接続できない。

**解決策**: appsettings.json設定を確認してください：

```json
{
  "Sekiban": {
    "Database": "Cosmos",  // または "Postgres"
    "Cosmos": {
      "ConnectionString": "your-connection-string",
      "DatabaseName": "your-database-name"
    }
  }
}
```

そして、データベースのセットアップが正しいことを確認します：

```csharp
// Cosmos DB用
builder.AddSekibanCosmosDb();

// PostgreSQL用
builder.AddSekibanPostgresDb();
```

## 10. テストの問題

**問題**: シリアライゼーション例外でテストが失敗する。

**解決策**: シリアライゼーションをテストするために `CheckSerializability` メソッドを使用します：

```csharp
[Fact]
public void TestSerializable()
{
    CheckSerializability(new YourCommand(...));
    CheckSerializability(new YourEvent(...));
}
```

## 11. パフォーマンスの問題

**問題**: 大きなイベントストリームでのパフォーマンスが遅い。

**解決策**:

1. イベントスナップショットの実装を検討する
2. 適切なデータベースインデックスを使用する
3. 特定の読み取りパターンに合わせてクエリを最適化する
4. 異なるクエリニーズに対して複数のプロジェクションの使用を検討する
5. 大きな結果セットにはページネーションを使用する

## 12. 並行処理の問題

**問題**: 同時実行例外でコマンドが失敗する。

**解決策**: 楽観的並行性制御を実装します：

```csharp
public ResultBox<EventOrNone> Handle(YourCommand command, ICommandContext<YourAggregateType> context)
{
    // 期待されるバージョンをチェック
    if (context.GetAggregate().Version != command.ExpectedVersion)
    {
        return new ConcurrencyException(
            $"期待されるバージョン{command.ExpectedVersion}ですが、{context.GetAggregate().Version}が見つかりました");
    }
    
    // コマンド処理を続行
    return EventOrNone.Event(new YourEvent(...));
}
```

## 13. APIエンドポイントの問題

**問題**: APIエンドポイントが500 Internal Server Errorを返す。

**解決策**: エンドポイントのエラー処理を改善します：

```csharp
apiRoute.MapPost("/command",
    async ([FromBody] YourCommand command, [FromServices] SekibanOrleansExecutor executor) =>
    {
        try
        {
            var result = await executor.CommandAsync(command);
            return result.Match(
                success => Results.Ok(success),
                error => Results.Problem(error.Error.Message)
            );
        }
        catch (Exception ex)
        {
            // 例外をログに記録
            return Results.Problem(
                title: "予期しないエラーが発生しました",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    });
```

## 14. 依存性注入の問題

**問題**: `System.InvalidOperationException: 型 'YourDomainDomainTypes' のサービスがありません`

**解決策**: Program.csでドメインタイプを登録していることを確認してください：

```csharp
builder.Services.AddSingleton(
    YourDomainDomainTypes.Generate(
        YourDomainEventsJsonContext.Default.Options));
```

## 15. アプリケーションの実行

**問題**: Aspireホストでアプリケーションを実行する際の問題。

**解決策**: 次のコマンドを使用してください：

```bash
dotnet run --project MyProject.AppHost
```

HTTPSプロファイルで起動するには：

```bash
dotnet run --project MyProject.AppHost --launch-profile https
```

## 16. ISekibanExecutor vs. SekibanOrleansExecutor

**問題**: どのエグゼキューター型を使用すべきかわからない。

**解決策**: ドメインサービスやワークフローを実装する場合、より良いテスタビリティのために具象 `SekibanOrleansExecutor` クラスではなく `ISekibanExecutor` インターフェースを使用してください。`ISekibanExecutor` インターフェースは `Sekiban.Pure.Executors` 名前空間にあります。

```csharp
// テスタビリティに優れている
public static async Task<r> YourWorkflow(
    YourCommand command,
    ISekibanExecutor executor)
{
    // 実装
}

// 以下の代わりに
public static async Task<r> YourWorkflow(
    YourCommand command,
    SekibanOrleansExecutor executor)
{
    // 実装
}
```

## 17. SekibanDateProducerの使用

**問題**: 外部システムからSekibanと一貫した時間取得方法が必要。

**解決策**: `SekibanDateProducer.GetRegistered().UtcNow` を使用することで、外部システムからもSekibanと同じ方法で時間を取得できます。これにより、テスト時のモック化や時間の一貫性を保つことができます：

```csharp
using Sekiban.Pure.DateProducer;

// Sekibanと一貫した時間取得
var currentTime = SekibanDateProducer.GetRegistered().UtcNow;

// テスト時にはモック実装に置き換え可能
public class MockDateProducer : ISekibanDateProducer
{
    private readonly DateTime _fixedTime;
    
    public MockDateProducer(DateTime fixedTime)
    {
        _fixedTime = fixedTime;
    }
    
    public DateTime UtcNow => _fixedTime;
    public DateTime Now => _fixedTime.ToLocalTime();
    public DateTime Today => _fixedTime.Date;
}

// テストでの使用例
SekibanDateProducer.Register(new MockDateProducer(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
```