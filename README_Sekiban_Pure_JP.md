# Sekiban イベントソーシング ガイド 開発者向け

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

#### コマンドでのアグリゲートペイロードへのアクセス

コマンドハンドラーでアグリゲートペイロードにアクセスする方法は、2つまたは3つのジェネリックパラメータバージョンを使用するかによって2つあります：

1. **型制約あり（3つのジェネリックパラメータ）**：

   ```csharp
   // ICommandWithHandler<TCommand, TProjector, TAggregatePayload>を使用
   public ResultBox<EventOrNone> Handle(YourCommand command, ICommandContext<ConfirmedUser> context)
   {
       // 強く型付けされたアグリゲートとペイロードに直接アクセス
       var aggregate = context.GetAggregate();
       var payload = aggregate.Payload; // すでにConfirmedUserとして型付けされている
       
       // ペイロードのプロパティを直接使用
       var userName = payload.Name;
       
       return EventOrNone.Event(new YourEvent(...));
   }
   ```

2. **型制約なし（2つのジェネリックパラメータ）**：

   ```csharp
   // ICommandWithHandler<TCommand, TProjector>を使用
   public ResultBox<EventOrNone> Handle(YourCommand command, ICommandContext<IAggregatePayload> context)
   {
       // ペイロードを期待される型にキャストする必要がある
       if (context.GetAggregate().GetPayload() is ConfirmedUser payload)
       {
           // これで型付けされたペイロードを使用できる
           var userName = payload.Name;
           
           return EventOrNone.Event(new YourEvent(...));
       }
       
       // ペイロードが期待される型でない場合を処理
       return new SomeException("ConfirmedUser状態が必要です");
   }
   ```

アグリゲートが特定の状態であることが分かっている場合は、コンパイル時の安全性とよりクリーンなコードを提供する3パラメータバージョンが推奨されます。

#### コマンドから複数のイベントを生成する

コマンドが複数のイベントを生成する必要がある場合は、コマンドコンテキストの`AppendEvent`メソッドを使用できます：

```csharp
public ResultBox<EventOrNone> Handle(ComplexCommand command, ICommandContext<TAggregatePayload> context)
{
    // まず、イベントを1つずつ追加する
    context.AppendEvent(new FirstEventHappened(command.SomeData));
    context.AppendEvent(new SecondEventHappened(command.OtherData));
    
    // すべてのイベントが追加されたことを示すためにEventOrNone.Noneを返す
    return EventOrNone.None;
    
    // または、最後のイベントを返すこともできる
    // return EventOrNone.Event(new FinalEventHappened(command.FinalData));
}
```

ポイント：

- `context.AppendEvent(eventPayload)`を使用してイベントストリームにイベントを追加する
- 複数のイベントを順番に追加できる
- すべてのイベントが`AppendEvent`を使用して追加された場合は`EventOrNone.None`を返す
- または、その方法を好む場合は`EventOrNone.Event`を使用して最後のイベントを返す
- 追加されたすべてのイベントは、追加された順序でアグリゲートに適用される

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
   - インメモリまたはOrleansベースのテスト用の組み込みテストフレームワークを使用
   - 流暢なテストアサーションのためにResultBoxを使用したメソッドチェーンを活用

6. **エラー処理**：
   - `ResultBox`を使用してエラーを処理し、意味のあるメッセージを返す
   - イベントを生成する前にコマンドを検証
   - プロジェクターでエッジケースを処理

## Sekibanにおけるユニットテスト

Sekibanは、イベントソースアプリケーションのユニットテストをインメモリとOrleansベースの両方のテストフレームワークでサポートしています。

### インメモリテスト

シンプルなユニットテストには、`Sekiban.Pure.xUnit`名前空間の`SekibanInMemoryTestBase`クラスを使用できます：

```csharp
public class YourTests : SekibanInMemoryTestBase
{
    // ドメインタイプを提供するためにオーバーライド
    protected override SekibanDomainTypes GetDomainTypes() => 
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options);

    [Fact]
    public void SimpleTest()
    {
        // Given - コマンドを実行してレスポンスを取得
        var response1 = GivenCommand(new CreateYourEntity("Name", "Value"));
        Assert.Equal(1, response1.Version);

        // When - 同じアグリゲートに対して別のコマンドを実行
        var response2 = WhenCommand(new UpdateYourEntity(response1.PartitionKeys.AggregateId, "NewValue"));
        Assert.Equal(2, response2.Version);

        // Then - アグリゲートを取得して状態を検証
        var aggregate = ThenGetAggregate<YourEntityProjector>(response2.PartitionKeys);
        var entity = (YourEntity)aggregate.Payload;
        Assert.Equal("NewValue", entity.Value);
        
        // Then - クエリを実行して結果を検証
        var queryResult = ThenQuery(new YourEntityExistsQuery("Name"));
        Assert.True(queryResult);
    }
}
```

ベースクラスはGiven-When-Thenパターンに従うメソッドを提供します：

- `GivenCommand` - コマンドを実行して初期状態を設定
- `WhenCommand` - テスト対象のコマンドを実行
- `ThenGetAggregate` - アグリゲートを取得して状態を検証
- `ThenQuery` - クエリを実行して結果を検証

### ResultBoxを使用したメソッドチェーン

より流暢で読みやすいテストのために、メソッドチェーンをサポートするResultBoxベースのメソッドを使用できます：

```csharp
[Fact]
public void ChainedTest()
    => GivenCommandWithResult(new CreateYourEntity("Name", "Value"))
        .Do(response => Assert.Equal(1, response.Version))
        .Conveyor(response => WhenCommandWithResult(new UpdateYourEntity(response.PartitionKeys.AggregateId, "NewValue")))
        .Do(response => Assert.Equal(2, response.Version))
        .Conveyor(response => ThenGetAggregateWithResult<YourEntityProjector>(response.PartitionKeys))
        .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<YourEntity>())
        .Do(payload => Assert.Equal("NewValue", payload.Value))
        .Conveyor(_ => ThenQueryWithResult(new YourEntityExistsQuery("Name")))
        .Do(Assert.True)
        .UnwrapBox();
```

ポイント：

- `Conveyor`は一つの操作の結果を次の入力に変換
- `Do`はアサーションやサイドエフェクトを実行し、結果を変更しない
- `UnwrapBox`は最終的なResultBoxをアンラップし、いずれかのステップが失敗した場合は例外をスロー

### Orleansテスト

Orleans統合のテストには、`Sekiban.Pure.Orleans.xUnit`名前空間の`SekibanOrleansTestBase`クラスを使用します：

```csharp
public class YourOrleansTests : SekibanOrleansTestBase<YourOrleansTests>
{
    public override SekibanDomainTypes GetDomainTypes() => 
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options);

    [Fact]
    public void OrleansTest() =>
        GivenCommandWithResult(new CreateYourEntity("Name", "Value"))
            .Do(response => Assert.Equal(1, response.Version))
            .Conveyor(response => WhenCommandWithResult(new UpdateYourEntity(response.PartitionKeys.AggregateId, "NewValue")))
            .Do(response => Assert.Equal(2, response.Version))
            .Conveyor(response => ThenGetAggregateWithResult<YourEntityProjector>(response.PartitionKeys))
            .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<YourEntity>())
            .Do(payload => Assert.Equal("NewValue", payload.Value))
            .Conveyor(_ => ThenGetMultiProjectorWithResult<AggregateListProjector<YourEntityProjector>>())
            .Do(projector => 
            {
                Assert.Equal(1, projector.Aggregates.Values.Count());
                var entity = (YourEntity)projector.Aggregates.Values.First().Payload;
                Assert.Equal("NewValue", entity.Value);
            })
            .UnwrapBox();
            
    [Fact]
    public void TestSerializable()
    {
        // コマンドがシリアル化可能であることをテスト（Orleansでは重要）
        CheckSerializability(new CreateYourEntity("Name", "Value"));
    }
}
```

Orleansテストベースクラスはインメモリテストベースクラスと同様のメソッドを提供しますが、より現実的なテストのために完全なOrleansテストクラスターをセットアップします。

### InMemorySekibanExecutorを使用した手動テスト

より複雑なシナリオやカスタムテストセットアップには、`InMemorySekibanExecutor`を手動で作成できます：

```csharp
[Fact]
public async Task ManualExecutorTest()
{
    // インメモリエグゼキューターを作成
    var executor = new InMemorySekibanExecutor(
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options),
        new FunctionCommandMetadataProvider(() => "test"),
        new Repository(),
        new ServiceCollection().BuildServiceProvider());

    // コマンドを実行
    var result = await executor.CommandAsync(new CreateYourEntity("Name", "Value"));
    Assert.True(result.IsSuccess);
    var value = result.GetValue();
    Assert.NotNull(value);
    Assert.Equal(1, value.Version);
    var aggregateId = value.PartitionKeys.AggregateId;

    // アグリゲートをロード
    var aggregateResult = await executor.LoadAggregateAsync<YourEntityProjector>(
        PartitionKeys.Existing<YourEntityProjector>(aggregateId));
    Assert.True(aggregateResult.IsSuccess);
    var aggregate = aggregateResult.GetValue();
    var entity = (YourEntity)aggregate.Payload;
    Assert.Equal("Name", entity.Name);
    Assert.Equal("Value", entity.Value);
}
```

### テストのベストプラクティス

1. **コマンドのテスト**: コマンドが期待されるイベントと状態変更を生成することを検証
2. **プロジェクターのテスト**: プロジェクターがイベントを正しく適用してアグリゲート状態を構築することを検証
3. **クエリのテスト**: クエリが現在の状態に基づいて期待される結果を返すことを検証
4. **状態遷移のテスト**: 特に異なるペイロードタイプを使用する場合、状態遷移が正しく機能することを検証
5. **エラーケースのテスト**: 検証が失敗した場合にコマンドが適切に失敗することを検証
6. **シリアル化のテスト**: Orleansテストでは、コマンドとイベントがシリアル化可能であることを検証

## Sekibanプロジェクトの作成と使用

### 1. プロジェクトセットアップ

テンプレートから始めます：

```bash
dotnet new install Sekiban.Pure.Templates
dotnet new sekiban-orleans-aspire -n MyProject
```

### 2. API設定

テンプレートは必要なすべての設定を含むProgram.csを生成します。以下はその仕組みです：

```csharp
var builder = WebApplication.CreateBuilder(args);

// AspireとOrleans統合を追加
builder.AddServiceDefaults();
builder.UseOrleans(config =>
{
    config.UseDashboard(options => { });
    config.AddMemoryStreams("EventStreamProvider")
          .AddMemoryGrainStorage("EventStreamProvider");
});

// ドメインタイプとシリアル化を登録
builder.Services.AddSingleton(
    OrleansSekibanDomainDomainTypes.Generate(
        OrleansSekibanDomainEventsJsonContext.Default.Options));

// データベース（Cosmos DBまたはPostgreSQL）を設定
if (builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower() == "cosmos")
{
    builder.AddSekibanCosmosDb();
} else
{
    builder.AddSekibanPostgresDb();
}
```

### 3. APIエンドポイント

コマンドとクエリのエンドポイントをマッピングします：

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
            await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox())
    .WithOpenApi();
```

### ToSimpleCommandResponse()を使用した効率的なAPIエンドポイント

コマンドを実行するAPIエンドポイントを作成する際に、`ToSimpleCommandResponse()`拡張メソッドを使用すると以下のようなメリットがあります：

1. **ペイロードサイズの削減**: 完全なCommandResponse（すべてのイベントを含む）からコンパクトなCommandResponseSimpleに変換します
2. **LastSortableUniqueIdへの簡単なアクセス**: クライアント側の一貫性確保に重要な情報を抽出します
3. **クリーンなAPI設計**: `UnwrapBox()`と組み合わせることで、クリーンで一貫性のあるAPIレスポンスを作成します

#### 実装例

```csharp
apiRoute
    .MapPost(
        "/inputweatherforecast",
        async (
                [FromBody] InputWeatherForecastCommand command,
                [FromServices] SekibanOrleansExecutor executor) =>
            await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox())
    .WithName("InputWeatherForecast")
    .WithOpenApi();
```

#### クライアント側での使用

コマンドを実行した後、返された`LastSortableUniqueId`を使用して、後続のクエリが更新された状態を確実に取得できるようにします：

```csharp
// コマンドを実行
var response = await weatherApiClient.InputWeatherAsync(new InputWeatherForecastCommand(...));

// LastSortableUniqueIdを後続のクエリで使用
var forecasts = await weatherApiClient.GetWeatherAsync(
    waitForSortableUniqueId: response.LastSortableUniqueId);
```

このパターンを使用することで、UIが常に最新の状態変更を反映し、より一貫性のあるユーザー体験を提供できます。

ポイント：

- コマンドとクエリの処理には`SekibanOrleansExecutor`を使用
- コマンドはPOSTエンドポイントにマッピング
- クエリは通常GETエンドポイントにマッピング
- 結果は`UnwrapBox()`を使用して`ResultBox`からアンラップ
- OpenAPIサポートはデフォルトで含まれる
- コマンド実行結果は`ToSimpleCommandResponse()`を使用して簡略化することで、LastSortableUniqueIdなどの重要情報を簡単に取得可能

### 4. 実装ステップ

1. プロジェクトテンプレートから始める
2. ドメインモデル（アグリゲート）を定義
3. ユーザーの意図を表すコマンドを作成
4. 状態変更を表すイベントを定義
5. イベントをアグリゲートに適用するプロジェクターを実装
6. データを取得およびフィルタリングするクエリを作成
7. JSONシリアル化コンテキストを設定
8. `SekibanOrleansExecutor`を使用してAPIエンドポイントをマッピング

### 5. 設定オプション

テンプレートは2つのデータベースオプションをサポートしています：

```json
{
  "Sekiban": {
    "Database": "Cosmos"  // または "Postgres"
  }
}
```

## Webフロントエンド実装

ドメイン用のWebフロントエンドを実装するには：

1. WebプロジェクトでAPIクライアントを作成します：

```csharp
public class YourApiClient(HttpClient httpClient)
{
    public async Task<YourQuery.ResultRecord[]> GetItemsAsync(
        CancellationToken cancellationToken = default)
    {
        List<YourQuery.ResultRecord>? items = null;

        await foreach (var item in httpClient.GetFromJsonAsAsyncEnumerable<YourQuery.ResultRecord>("/api/items", cancellationToken))
        {
            if (item is not null)
            {
                items ??= [];
                items.Add(item);
            }
        }

        return items?.ToArray() ?? [];
    }

    public async Task CreateItemAsync(
        string param1,
        string param2,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateYourItemCommand(param1, param2);
        await httpClient.PostAsJsonAsync("/api/createitem", command, cancellationToken);
    }
}
```

1. Program.csでAPIクライアントを登録します：

```csharp
builder.Services.AddHttpClient<YourApiClient>(client =>
{
    client.BaseAddress = new("https+http://apiservice");
});
```

1. ドメインとやり取りするためのRazorページを作成します

## SekibanDomainTypesとソース生成

### SekibanDomainTypesの理解

Sekibanはビルド時にドメインタイプ登録を作成するためにソース生成を使用します。これはドメインモデル登録を簡素化し、型安全性を確保するフレームワークの重要な部分です。

```csharp
// このクラスはSekiban.Pure.SourceGeneratorによって自動生成されます
// 手動で作成する必要はありません
public static class YourProjectDomainDomainTypes
{
    // DIコンテナにドメインタイプを登録するために使用
    public static SekibanDomainTypes Generate(JsonSerializerOptions options) => 
        // 実装はドメインモデルに基づいて生成される
        ...

    // シリアル化チェックに使用
    public static SekibanDomainTypes Generate() => 
        Generate(new JsonSerializerOptions());
}
```

### ソース生成に関する重要なポイント

1. **命名規則**：
   - 生成されるクラスは`[ProjectName]DomainDomainTypes`パターンに従います
   - 例えば、「SchoolManagement」という名前のプロジェクトは`SchoolManagementDomainDomainTypes`を持ちます

2. **名前空間**：
   - 生成されるクラスは`[ProjectName].Generated`名前空間に配置されます
   - 例えば、`SchoolManagement.Domain.Generated`

3. **アプリケーションでの使用法**：

   ```csharp
   // Program.csで
   builder.Services.AddSingleton(
       YourProjectDomainDomainTypes.Generate(
           YourProjectDomainEventsJsonContext.Default.Options));
   ```

4. **テストでの使用法**：

   ```csharp
   // テストクラスで
   protected override SekibanDomainTypes GetDomainTypes() => 
       YourProjectDomainDomainTypes.Generate(
           YourProjectDomainEventsJsonContext.Default.Options);
   ```

5. **テストに必要なインポート**：

   ```csharp
   using YourProject.Domain;
   using YourProject.Domain.Generated; // 生成された型を含む
   using Sekiban.Pure;
   using Sekiban.Pure.xUnit;
   ```

### ソース生成のトラブルシューティング

1. **生成された型が見つからない**：
   - テストを実行する前にプロジェクトが正常にビルドされていることを確認
   - すべてのドメイン型に必要な属性があることを確認
   - ソース生成に関連するビルド警告を確認

2. **名前空間エラー**：
   - 正しい生成された名前空間をインポートしていることを確認
   - 名前空間はソースファイルでは表示されず、コンパイルされたアセンブリでのみ表示される

3. **型が見つからないエラー**：
   - 正しい命名規則を使用していることを確認
   - クラス名の入力ミスを確認

4. **テストのベストプラクティス**：
   - 常にソース生成された型を直接参照する
   - テスト用に独自のドメイン型クラスを作成しない
   - メインアプリケーションと同じJsonSerializerOptionsを使用する

## ワークフローとドメインサービス

Sekibanは、複数のアグリゲートにまたがるビジネスロジックや特殊な処理を必要とするビジネスロジックをカプセル化するドメインワークフローとサービスの実装をサポートしています。

### ドメインワークフロー

ドメインワークフローは、複数のアグリゲートを含むビジネスプロセスや複雑な検証ロジックを実装するステートレスサービスです。特に以下の場合に有用です：

1. **クロスアグリゲート操作**: ビジネスプロセスが複数のアグリゲートにまたがる場合
2. **複雑な検証**: 検証が複数のアグリゲートや外部システムに対するチェックを必要とする場合
3. **再利用可能なビジネスロジック**: 同じロジックが複数の場所で使用される場合

```csharp
// 重複チェック用のドメインワークフローの例
namespace YourProject.Domain.Workflows;

public static class DuplicateCheckWorkflows
{
    // 重複チェック操作の結果型
    public class DuplicateCheckResult
    {
        public bool IsDuplicate { get; }
        public string? ErrorMessage { get; }
        public object? CommandResult { get; }

        private DuplicateCheckResult(bool isDuplicate, string? errorMessage, object? commandResult)
        {
            IsDuplicate = isDuplicate;
            ErrorMessage = errorMessage;
            CommandResult = commandResult;
        }

        public static DuplicateCheckResult Duplicate(string errorMessage) => 
            new(true, errorMessage, null);

        public static DuplicateCheckResult Success(object commandResult) => 
            new(false, null, commandResult);
    }

    // 登録前にIDの重複をチェックするワークフローメソッド
    public static async Task<DuplicateCheckResult> CheckUserIdDuplicate(
        RegisterUserCommand command,
        ISekibanExecutor executor)
    {
        // userIdが既に存在するかチェック
        var userIdExists = await executor.QueryAsync(new UserIdExistsQuery(command.UserId)).UnwrapBox();
        if (userIdExists)
        {
            return DuplicateCheckResult.Duplicate($"ID '{command.UserId}' を持つユーザーは既に存在します");
        }
        
        // 重複がなければコマンドを実行
        var result = await executor.CommandAsync(command).UnwrapBox();
        return DuplicateCheckResult.Success(result);
    }
}
```

**ポイント**:

- ワークフローは静的クラスと静的メソッド、またはインスタンスベースのクラスと依存性注入のいずれでも実装できます 🏗️
- `Workflows`フォルダーまたは名前空間に配置する必要があります 📁
- より良いテスト可能性のために`ISekibanExecutor`インターフェースを使用する必要があります 🧪
- 成功/失敗情報をカプセル化するドメイン固有の結果型を返す必要があります 📊
- APIエンドポイントや他のサービスから呼び出すことができます 🔗

### APIエンドポイントでのワークフローの使用

```csharp
// Program.csで
apiRoute.MapPost("/users/register",
    async ([FromBody] RegisterUserCommand command, [FromServices] SekibanOrleansExecutor executor) => 
    {
        // 重複をチェックするためにワークフローを使用
        var result = await DuplicateCheckWorkflows.CheckUserIdDuplicate(command, executor);
        if (result.IsDuplicate)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "ユーザーIDの重複",
                detail: result.ErrorMessage);
        }
        return result.CommandResult;
    });
```

### ワークフローのテスト

ワークフローは、他のSekibanコンポーネントと同じインメモリテストアプローチを使用してテストできます：

```csharp
public class DuplicateCheckWorkflowsTests : SekibanInMemoryTestBase
{
    protected override SekibanDomainTypes GetDomainTypes() => 
        YourDomainDomainTypes.Generate(YourDomainEventsJsonContext.Default.Options);

    [Fact]
    public async Task CheckUserIdDuplicate_WhenUserIdExists_ReturnsDuplicate()
    {
        // Arrange - テストしたいIDを持つユーザーを作成
        var existingUserId = "U12345";
        var command = new RegisterUserCommand(
            "John Doe",
            existingUserId,
            "john@example.com");

        // 同じIDを持つユーザーが存在することを確認するために登録
        GivenCommand(command);

        // Act - 同じIDで別のユーザーを登録しようとする
        var result = await DuplicateCheckWorkflows.CheckUserIdDuplicate(command, Executor);

        // Assert
        Assert.True(result.IsDuplicate);
        Assert.Contains(existingUserId, result.ErrorMessage);
        Assert.Null(result.CommandResult);
    }

    [Fact]
    public async Task CheckUserIdDuplicate_WhenUserIdDoesNotExist_ReturnsSuccess()
    {
        // Arrange
        var newUserId = "U67890";
        var command = new RegisterUserCommand(
            "Jane Doe",
            newUserId,
            "jane@example.com");

        // Act
        var result = await DuplicateCheckWorkflows.CheckUserIdDuplicate(command, Executor);

        // Assert
        Assert.False(result.IsDuplicate);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.CommandResult);
    }
}
```

**ポイント**:

- ワークフローのテストには`SekibanInMemoryTestBase`を使用
- ベースクラスは`ISekibanExecutor`を実装する`Executor`プロパティを提供
- テスト状態を設定するには`GivenCommand`を使用
- 成功と失敗の両方のシナリオをテスト

## 一般的な問題と解決策

1. **名前空間エラー**: `Sekiban.Core.*`ではなく`Sekiban.Pure.*`名前空間を使用していることを確認してください。

2. **コマンドコンテキスト**: コマンドコンテキストはアグリゲートペイロードを直接公開しません。アグリゲート状態をチェックする必要がある場合は、コマンドハンドラーでパターンマッチングを使用してください：

   ```csharp
   if (context.AggregatePayload is YourAggregate aggregate)
   {
       // アグリゲートプロパティを使用
   }
   ```

3. **アプリケーションの実行**: Aspireホストでアプリケーションを実行するには、次のコマンドを使用します：

```bash
dotnet run --project MyProject.AppHost
```

HTTPSプロファイルでAppHostを起動するには、次を使用します：

```bash
dotnet run --project MyProject.AppHost --launch-profile https
```

これにより、アプリケーションがHTTPSを使用して安全に通信することが保証されます。これは特に本番環境で重要です。

1. **Webフロントエンドへのアクセス**: WebフロントエンドはAspireダッシュボードに表示されるURLで利用可能で、通常は`https://localhost:XXXXX`のようなURLです。

2. **ISekibanExecutor vs. SekibanOrleansExecutor**: ドメインサービスやワークフローを実装する場合、より良いテスト可能性のために具体的な`SekibanOrleansExecutor`クラスではなく`ISekibanExecutor`インターフェースを使用してください。`ISekibanExecutor`インターフェースは`Sekiban.Pure.Executors`名前空間にあります。

## 高度なクエリ機能

### 特定のイベントを待機する

イベントソーシングシステムでは、特に分散システムにおいて、コマンドが処理されてからクエリがそのコマンドの結果を反映するまでにタイムラグが生じることがあります。Sekibanでは、クエリが結果を返す前に特定のイベントが処理されるのを待機するメカニズムを提供しています。

#### IWaitForSortableUniqueIdインターフェース

```csharp
public interface IWaitForSortableUniqueId
{
    string? WaitForSortableUniqueId { get; }
}
```

このインターフェースを使用すると、クエリオブジェクトは実行前に特定のイベント（そのソート可能な一意のIDで識別される）を待機するよう指定できます。

#### 待機クエリの実装

特定のイベントを待機できるクエリを実装するには：

```csharp
[GenerateSerializer]
public record WeatherForecastQuery(string QueryId) : 
    IMultiProjectionListQuery<AggregateListProjector<WeatherForecastProjector>, 
                             WeatherForecastQuery, 
                             WeatherForecastQuery.WeatherForecastRecord>,
    IWaitForSortableUniqueId
{
    public string? WaitForSortableUniqueId { get; set; }

    // クエリ処理の実装...
    public static ResultBox<IEnumerable<WeatherForecastRecord>> HandleFilter(
        MultiProjectionState<AggregateListProjector<WeatherForecastProjector>> state,
        WeatherForecastQuery query,
        IQueryContext context)
    {
        // 実装の詳細...
    }

    // 結果の型
    [GenerateSerializer]
    public record WeatherForecastRecord(
        string Id,
        string Location,
        DateOnly Date,
        int TemperatureC,
        string Summary);
}
```

#### APIクライアントでの待機機能の使用

APIクライアントを実装する場合：

```csharp
public async Task<WeatherForecastRecord[]> GetWeatherAsync(
    int maxItems = 10, 
    string? waitForSortableUniqueId = null, 
    CancellationToken cancellationToken = default)
{
    var requestUri = string.IsNullOrEmpty(waitForSortableUniqueId)
        ? "/api/weatherforecast"
        : $"/api/weatherforecast?waitForSortableUniqueId={Uri.EscapeDataString(waitForSortableUniqueId)}";
        
    // HTTPリクエストを行う...
}
```

#### API設定

APIエンドポイントが待機パラメータを受け入れて使用するように設定します：

```csharp
apiRoute
    .MapGet(
        "/weatherforecast",
        async ([FromQuery] string? waitForSortableUniqueId, [FromServices] SekibanOrleansExecutor executor) =>
        {
            var query = new WeatherForecastQuery("")
            {
                WaitForSortableUniqueId = waitForSortableUniqueId
            };
            var list = await executor.QueryAsync(query).UnwrapBox();
            return list.Items;
        });
```

#### 即時更新のためのクライアント側の実装

コマンドを実行した後、後続のクエリを行う際にコマンドの `LastSortableUniqueId` プロパティを使用します：

```csharp
// コマンドを実行
var response = await WeatherApi.UpdateLocationAsync(
    weatherForecastId,
    newLocation);
    
// LastSortableUniqueIdを使用してクエリを実行し、更新された状態を確実に取得
var forecasts = await WeatherApi.GetWeatherAsync(
    waitForSortableUniqueId: response.LastSortableUniqueId);
```

このパターンを使用することで、UIが常に最新の状態変更を反映し、よりユーザー体験の一貫性を保証します。

## 結論
