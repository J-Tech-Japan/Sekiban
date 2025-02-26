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

アグリゲートは2つの主要な部分で構成されています：

1. **アグリゲートペイロード**: すべてのアグリゲートに存在する基本情報：
   - 現在のバージョン
   - 最後のイベントID
   - その他のシステムレベルのメタデータ

2. **ペイロード**: 開発者が定義するドメイン固有のデータ

Sekibanでは、アグリゲートは`IAggregatePayload`を実装します：

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
- アグリゲートペイロードとドメインペイロードを組み合わせるために`IAggregatePayload`インターフェースを実装
- Orleansシリアル化のための`[GenerateSerializer]`属性を含める
- ドメイン固有のプロパティをコンストラクタパラメータとして定義
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
- `ICommandWithHandler<TCommand, TProjector>`インターフェースを実装、または状態ベースの制約を強制する必要がある場合は`ICommandWithHandler<TCommand, TProjector, TPayloadType>`インターフェースを実装
- `[GenerateSerializer]`属性を含める
- アグリゲートが保存される場所を決定する`SpecifyPartitionKeys`メソッドを定義：
  - 新しいアグリゲートの場合：`PartitionKeys.Generate<YourProjector>()`
  - 既存のアグリゲートの場合：`PartitionKeys.Existing<YourProjector>(aggregateId)`
- イベントを返す`Handle`メソッドを実装
- コマンドは直接状態を変更せず、イベントを生成する

#### 状態制約のための第3ジェネリックパラメータの使用

型レベルで状態ベースの制約を強制するために、第3のジェネリックパラメータを指定できます：

```csharp
public record RevokeUser(Guid UserId) : ICommandWithHandler<RevokeUser, UserProjector, ConfirmedUser>
{
    public PartitionKeys SpecifyPartitionKeys(RevokeUser command) => PartitionKeys<UserProjector>.Existing(UserId);
    
    public ResultBox<EventOrNone> Handle(RevokeUser command, ICommandContext<ConfirmedUser> context) =>
        context
            .GetAggregate()
            .Conveyor(_ => EventOrNone.Event(new UserUnconfirmed()));
}
```

ポイント：
- 第3ジェネリックパラメータ`ConfirmedUser`は、現在のアグリゲートペイロードが`ConfirmedUser`型である場合にのみこのコマンドを実行できることを指定します
- コマンドコンテキストは`ICommandContext<IAggregatePayload>`ではなく`ICommandContext<ConfirmedUser>`に強く型付けされています
- これにより、状態依存の操作にコンパイル時の安全性が提供されます
- エグゼキューターはコマンドを実行する前に、現在のペイロードタイプが指定されたタイプと一致するかどうかを自動的にチェックします
- これは、エンティティの異なる状態を表現するためにアグリゲートペイロードタイプを使用する場合に特に有用です

### 3. イベント

イベントは2つの主要な部分で構成されています：

1. **イベントメタデータ**: すべてのイベントに含まれるシステムレベルの情報：
   - パーティションキー
   - タイムスタンプ
   - ID
   - バージョン
   - その他のシステムメタデータ

2. **イベントペイロード**: 開発者が定義するドメイン固有のデータ

イベントは不変であり、システムで発生した事実を表します。開発者は`IEventPayload`を実装してイベントペイロードを定義することに注力します：

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
- ドメイン固有のイベントデータのために`IEventPayload`インターフェースを実装
- `[GenerateSerializer]`属性を含める
- 過去形で名前を付ける（例：「Inputted」、「Updated」、「Deleted」）
- 状態変更を再構築するために必要なすべてのデータを含める

### パーティションキー

パーティションキーはデータベース内でのデータの編成方法を定義し、3つのコンポーネントで構成されています：

1. **RootPartitionKey** (文字列):
   - マルチテナントアプリケーションでテナントキーとして使用可能
   - テナントやその他の高レベルの区分でデータを分離

2. **AggregateGroup** (文字列):
   - アグリゲートのグループを定義
   - 通常、プロジェクター名と一致
   - 関連するアグリゲートをまとめて整理

3. **AggregateId** (Guid):
   - 各アグリゲートインスタンスの一意の識別子
   - グループ内の特定のアグリゲートを特定するために使用

コマンドを実装する際、これらのパーティションキーは2つの方法で使用されます：
- 新しいアグリゲートの場合：`PartitionKeys.Generate<YourProjector>()`で新しいパーティションキーを生成
- 既存のアグリゲートの場合：`PartitionKeys.Existing<YourProjector>(aggregateId)`で既存のキーを使用

### 4. プロジェクター

プロジェクターはイベントをアグリゲートに適用して現在の状態を構築します。プロジェクターの重要な機能の1つは、状態遷移を表現するためにアグリゲートペイロードの型を変更できることです。これにより、コマンドで状態に依存した振る舞いが可能になります。

以下は、ユーザー登録フローにおける状態遷移の例です：

```csharp
public class UserProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) => (payload, ev.GetPayload()) switch
    {
        // 初期登録で未確認ユーザーを作成
        (EmptyAggregatePayload, UserRegistered registered) => new UnconfirmedUser(registered.Name, registered.Email),
        
        // 確認により未確認ユーザーを確認済みユーザーに変更
        (UnconfirmedUser unconfirmedUser, UserConfirmed) => new ConfirmedUser(
            unconfirmedUser.Name,
            unconfirmedUser.Email),
            
        // 確認解除により確認済みユーザーを未確認ユーザーに戻す
        (ConfirmedUser confirmedUser, UserUnconfirmed) => new UnconfirmedUser(confirmedUser.Name, confirmedUser.Email),
        
        _ => payload
    };
}
```

ポイント：
- `IAggregateProjector`インターフェースを実装
- パターンマッチングを使用して異なるイベントタイプを処理
- 状態遷移に基づいて異なるアグリゲートペイロードの型を返す：
  - 状態変更によってビジネスルールを強制できる（例：確認済みユーザーのみが特定の操作を実行可能）
  - コマンドは現在の状態の型をチェックして有効な操作を判断
  - 型システムがコンパイル時にビジネスルールを強制
- 初期状態の作成を処理（`EmptyAggregatePayload`から）
- 各状態変更に対して新しいインスタンスを作成して不変性を維持

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

## Sekibanプロジェクトの作成と使用

### 1. プロジェクトのセットアップ

テンプレートから始めます：
```bash
dotnet new install Sekiban.Pure.Templates
dotnet new sekiban-orleans-aspire -n MyProject
```

### 2. API設定

テンプレートは必要な設定がすべて含まれたProgram.csを生成します。以下がその仕組みです：

```csharp
var builder = WebApplication.CreateBuilder(args);

// AspireとOrleansの統合を追加
builder.AddServiceDefaults();
builder.UseOrleans(config =>
{
    config.UseDashboard(options => { });
    config.AddMemoryStreams("EventStreamProvider")
          .AddMemoryGrainStorage("EventStreamProvider");
});

// ドメインタイプとシリアル化の登録
builder.Services.AddSingleton(
    OrleansSekibanDomainDomainTypes.Generate(
        OrleansSekibanDomainEventsJsonContext.Default.Options));

// データベースの設定（Cosmos DBまたはPostgreSQL）
if (builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower() == "cosmos")
{
    builder.AddSekibanCosmosDb();
} else
{
    builder.AddSekibanPostgresDb();
}
```

### 3. APIエンドポイント

コマンドとクエリのエンドポイントをマッピング：

```csharp
// クエリエンドポイント
apiRoute.MapGet("/weatherforecast", 
    async ([FromServices]SekibanOrleansExecutor executor) =>
    {
        var list = await executor.QueryAsync(new WeatherForecastQuery(""))
                                .UnwrapBox();
        return list.Items;
    })
    .WithOpenApi();

// コマンドエンドポイント
apiRoute.MapPost("/inputweatherforecast",
    async (
        [FromBody] InputWeatherForecastCommand command,
        [FromServices] SekibanOrleansExecutor executor) => 
            await executor.CommandAsync(command).UnwrapBox())
    .WithOpenApi();
```

ポイント：
- コマンドとクエリの処理に`SekibanOrleansExecutor`を使用
- コマンドはPOSTエンドポイントにマッピング
- クエリは通常GETエンドポイントにマッピング
- 結果は`UnwrapBox()`を使用して`ResultBox`からアンラップ
- OpenAPIサポートがデフォルトで含まれる

### 4. 実装手順

1. プロジェクトテンプレートから始める
2. ドメインモデル（アグリゲート）を定義
3. ユーザーの意図を表すコマンドを作成
4. 状態変更を表すイベントを定義
5. イベントをアグリゲートに適用するプロジェクターを実装
6. データを取得およびフィルタリングするクエリを作成
7. JSONシリアル化コンテキストを設定
8. `SekibanOrleansExecutor`を使用してAPIエンドポイントをマッピング

### 5. 設定オプション

テンプレートは2つのデータベースオプションをサポートします：
```json
{
  "Sekiban": {
    "Database": "Cosmos"  // または "Postgres"
  }
}
```

## 結論

Sekibanは.NETアプリケーションでイベントソーシングを実装するための強力なフレームワークを提供します。このガイドで概説された主要コンポーネントとベストプラクティスを理解することで、LLMプログラミングエージェントはSekibanイベントソーシングプロジェクトを効果的に作成および維持できます。
