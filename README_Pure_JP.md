# Sekiban イベントソーシング ガイド（LLMプログラミングエージェント向け）

このガイドでは、`templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/OrleansSekiban.Domain`のテンプレート構造に基づいて、Sekibanを使用したイベントソーシングプロジェクトの作成と操作方法について説明します。

## Sekibanを始める

Orleans と Aspire 統合を備えた新しい Sekiban プロジェクトをすぐに作成するには：

```bash
# Sekibanテンプレートをインストール
dotnet new install Sekiban.Pure.Templates

# 新しいプロジェクトを作成
dotnet new sekiban-orleans-aspire -n MyProject
```

このテンプレートには以下が含まれています：
- Orleansのための.NET Aspireホスト
- クラスターストレージ
- グレイン永続ストレージ
- キューストレージ

## イベントソーシングとは？

イベントソーシングは以下の特徴を持つ設計パターンです：
- アプリケーションの状態変更はすべてイベントのシーケンスとして保存される
- これらのイベントが真実の源泉となる
- 現在の状態はイベントを再生することで導き出される
- イベントは不変であり、システムで発生した事実を表す

## Sekibanイベントソーシングフレームワーク

Sekibanは.NETイベントソーシングフレームワークであり：
- C#アプリケーションでのイベントソーシングの実装を簡素化
- 分散システム用のOrleansとの統合を提供
- 様々なストレージバックエンドをサポート
- ドメインモデルを定義するためのクリーンで型安全なAPIを提供

## Sekibanプロジェクトの主要コンポーネント

### 1. アグリゲート

アグリゲートは状態とビジネスルールをカプセル化するドメインエンティティです。Sekibanでは、アグリゲートは`IAggregatePayload`を実装します。

```csharp
[GenerateSerializer]
public record WeatherForecast(
    string Location,
    DateOnly Date,
    int TemperatureC,
    string Summary
) : IAggregatePayload
{
    public int GetTemperatureF()
    {
        return 32 + (int)(TemperatureC / 0.5556);
    }
}
```

ポイント：
- 不変性のためにC#レコードを使用
- `IAggregatePayload`インターフェースを実装
- Orleansシリアル化のための`[GenerateSerializer]`属性を含める
- プロパティをコンストラクタパラメータとして定義
- レコード内にドメインロジックメソッドを含める

### 2. コマンド

コマンドはシステム状態を変更するユーザーの意図を表します。何が起こるべきかを定義します。

```csharp
[GenerateSerializer]
public record InputWeatherForecastCommand(
    string Location,
    DateOnly Date,
    int TemperatureC,
    string Summary
) : ICommandWithHandler<InputWeatherForecastCommand, WeatherForecastProjector>
{
    public PartitionKeys SpecifyPartitionKeys(InputWeatherForecastCommand command) => 
        PartitionKeys.Generate<WeatherForecastProjector>();

    public ResultBox<EventOrNone> Handle(InputWeatherForecastCommand command, ICommandContext<IAggregatePayload> context)
        => EventOrNone.Event(new WeatherForecastInputted(command.Location, command.Date, command.TemperatureC, command.Summary));    
}
```

ポイント：
- 不変性のためにC#レコードを使用
- `ICommandWithHandler<TCommand, TProjector>`インターフェースを実装
- `[GenerateSerializer]`属性を含める
- アグリゲートが保存される場所を決定する`SpecifyPartitionKeys`メソッドを定義：
  - 新しいアグリゲートの場合：`PartitionKeys.Generate<YourProjector>()`
  - 既存のアグリゲートの場合：`PartitionKeys.Existing<YourProjector>(aggregateId)`
- イベントを返す`Handle`メソッドを実装
- コマンドは直接状態を変更せず、イベントを生成する

### 3. イベント

イベントはシステムで発生した事実を表します。イベントは不変であり、真実の源泉です。

```csharp
[GenerateSerializer]
public record WeatherForecastInputted(
    string Location,
    DateOnly Date,
    int TemperatureC,
    string Summary
) : IEventPayload;
```

ポイント：
- 不変性のためにC#レコードを使用
- `IEventPayload`インターフェースを実装
- `[GenerateSerializer]`属性を含める
- 過去形で名前を付ける（例：「Inputted」、「Updated」、「Deleted」）
- 状態変更を再構築するために必要なすべてのデータを含める

### 4. プロジェクター

プロジェクターはイベントをアグリゲートに適用して現在の状態を構築します。

```csharp
public class WeatherForecastProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
        => (payload, ev.GetPayload()) switch
        {
            (EmptyAggregatePayload, WeatherForecastInputted inputted) => new WeatherForecast(inputted.Location, inputted.Date, inputted.TemperatureC, inputted.Summary),
            (WeatherForecast forecast, WeatherForecastDeleted _) => new DeletedWeatherForecast(
                forecast.Location,
                forecast.Date,
                forecast.TemperatureC,
                forecast.Summary),
            (WeatherForecast forecast, WeatherForecastLocationUpdated updated) => forecast with { Location = updated.NewLocation },
            _ => payload
        };
}
```

ポイント：
- `IAggregateProjector`インターフェースを実装
- パターンマッチングを使用して異なるイベントタイプを処理
- 各イベントに対して新しいアグリゲート状態を返す
- 初期状態の作成を処理（`EmptyAggregatePayload`から）
- 不変更新のためにC#レコードの`with`構文を使用

### 5. クエリ

クエリはシステムからデータを取得およびフィルタリングする方法を定義します。

```csharp
[GenerateSerializer]
public record WeatherForecastQuery(string LocationContains)
    : IMultiProjectionListQuery<AggregateListProjector<WeatherForecastProjector>, WeatherForecastQuery, WeatherForecastQuery.WeatherForecastRecord>
{
    public static ResultBox<IEnumerable<WeatherForecastRecord>> HandleFilter(MultiProjectionState<AggregateListProjector<WeatherForecastProjector>> projection, WeatherForecastQuery query, IQueryContext context)
    {
        return projection.Payload.Aggregates.Where(m => m.Value.GetPayload() is WeatherForecast)
            .Select(m => ((WeatherForecast)m.Value.GetPayload(), m.Value.PartitionKeys))
            .Select((touple) => new WeatherForecastRecord(touple.PartitionKeys.AggregateId, touple.Item1.Location,
                touple.Item1.Date, touple.Item1.TemperatureC, touple.Item1.Summary, touple.Item1.GetTemperatureF()))
            .ToResultBox();
    }

    public static ResultBox<IEnumerable<WeatherForecastRecord>> HandleSort(IEnumerable<WeatherForecastRecord> filteredList, WeatherForecastQuery query, IQueryContext context)
    {
        return filteredList.OrderBy(m => m.Date).AsEnumerable().ToResultBox();
    }

    [GenerateSerializer]
    public record WeatherForecastRecord(
        Guid WeatherForecastId,
        string Location,
        DateOnly Date,
        int TemperatureC,
        string Summary,
        int TemperatureF
    );
}
```

ポイント：
- 適切なクエリインターフェース（例：`IMultiProjectionListQuery`）を実装
- フィルタとソートのメソッドを定義
- クエリ結果用のネストされたレコードを作成
- フィルタリングとソートにLINQを使用
- 結果を`ResultBox`でラップして返す

### 6. JSONシリアル化コンテキスト

AOTコンパイルとパフォーマンスのために、JSONシリアル化コンテキストを定義します。

```csharp
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]
[JsonSerializable(typeof(EventDocument<OrleansSekiban.Domain.WeatherForecastInputted>))]
[JsonSerializable(typeof(OrleansSekiban.Domain.WeatherForecastInputted))]
[JsonSerializable(typeof(EventDocument<OrleansSekiban.Domain.WeatherForecastDeleted>))]
[JsonSerializable(typeof(OrleansSekiban.Domain.WeatherForecastDeleted))]
[JsonSerializable(typeof(EventDocument<OrleansSekiban.Domain.WeatherForecastLocationUpdated>))]
[JsonSerializable(typeof(OrleansSekiban.Domain.WeatherForecastLocationUpdated))]
public partial class OrleansSekibanDomainEventsJsonContext : JsonSerializerContext
{
}
```

ポイント：
- シリアル化が必要なすべてのイベントタイプを含める
- `[JsonSourceGenerationOptions]`を使用してシリアル化を設定
- 部分クラスとして定義

## プロジェクト構造

典型的なSekibanイベントソーシングプロジェクトは以下の構造に従います：

```
YourProject.Domain/
├── Aggregates/
│   └── YourAggregate.cs
├── Commands/
│   ├── CreateYourAggregateCommand.cs
│   ├── UpdateYourAggregateCommand.cs
│   └── DeleteYourAggregateCommand.cs
├── Events/
│   ├── YourAggregateCreated.cs
│   ├── YourAggregateUpdated.cs
│   └── YourAggregateDeleted.cs
├── Projectors/
│   └── YourAggregateProjector.cs
├── Queries/
│   └── YourAggregateQuery.cs
└── YourProjectDomainEventsJsonContext.cs
```

## LLMプログラミングエージェントのためのベストプラクティス

Sekibanイベントソーシングプロジェクトを扱う際：

1. **ドメインモデルを理解する**：
   - 主要なアグリゲートとその関係を特定する
   - ビジネスルールと制約を理解する

2. **イベントソーシングパターンに従う**：
   - コマンドは検証してイベントを生成する
   - イベントは不変であり、事実を表す
   - 状態はイベントから導き出される
   - クエリは投影された状態から読み取る

3. **命名規則**：
   - コマンド：命令形動詞（Create、Update、Delete）
   - イベント：過去形動詞（Created、Updated、Deleted）
   - アグリゲート：ドメインエンティティを表す名詞
   - プロジェクター：投影するアグリゲートにちなんで命名

4. **コード生成**：
   - Orleansシリアル化のための`[GenerateSerializer]`属性を使用
   - 各コンポーネントに適切なインターフェースを実装
   - 不変性のためにC#レコードを使用

5. **テスト**：
   - コマンドが生成するイベントを検証してテスト
   - イベントを適用して結果の状態をチェックしてプロジェクターをテスト
   - テストデータを設定して結果を検証してクエリをテスト

6. **エラー処理**：
   - `ResultBox`を使用してエラーを処理し、意味のあるメッセージを返す
   - イベントを生成する前にコマンドを検証
   - プロジェクターでエッジケースを処理

## 新しいSekibanプロジェクトの作成

1. 適切なプロジェクトテンプレートから始める
2. ドメインモデル（アグリゲート）を定義
3. ユーザーの意図を表すコマンドを作成
4. 状態変更を表すイベントを定義
5. イベントをアグリゲートに適用するプロジェクターを実装
6. データを取得およびフィルタリングするクエリを作成
7. JSONシリアル化コンテキストを設定

## 結論

Sekibanは.NETアプリケーションでイベントソーシングを実装するための強力なフレームワークを提供します。このガイドで概説された主要コンポーネントとベストプラクティスを理解することで、LLMプログラミングエージェントはSekibanイベントソーシングプロジェクトを効果的に作成および維持できます。
