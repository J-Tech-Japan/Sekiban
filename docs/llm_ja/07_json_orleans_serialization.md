# JSONとOrleansのシリアライゼーション - Sekiban イベントソーシング

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [集約、プロジェクター、コマンド、イベント](03_aggregate_command_events.md)
> - [複数集約プロジェクター](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [ワークフロー](06_workflow.md)
> - [JSONとOrleansのシリアライゼーション](07_json_orleans_serialization.md) (現在のページ)
> - [API実装](08_api_implementation.md)
> - [クライアントAPI (Blazor)](09_client_api_blazor.md)
> - [Orleansセットアップ](10_orleans_setup.md)
> - [ユニットテスト](11_unit_testing.md)
> - [一般的な問題と解決策](12_common_issues.md)
> - [ResultBox](13_result_box.md)

## JSONコンテキスト（AOTコンパイル用）

Sekibanでは、JSONシリアライズはAhead-of-Time（AOT）コンパイルのためにSystem.Text.Jsonのソース生成を通じて処理されます。これにより、より良いパフォーマンスとネイティブAOTシナリオとの互換性が提供されます。

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Sekiban.Core.Events;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]
[JsonSerializable(typeof(EventDocument<YourEvent>))]
[JsonSerializable(typeof(YourEvent))]
// すべてのイベントタイプを追加
public partial class YourDomainEventsJsonContext : JsonSerializerContext
{
}
```

**必須**:
- すべてのイベントタイプを含める
- `[JsonSourceGenerationOptions]` 属性を追加する
- partialクラスとして定義する

## 例：ドメインの完全なJSONコンテキスト

サンプルドメインのすべてのタイプを登録する方法を示す、より完全な例を以下に示します：

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Sekiban.Core.Events;
using YourProject.Domain.Aggregates.Orders.Events;
using YourProject.Domain.Aggregates.Products.Events;
using YourProject.Domain.Aggregates.Users.Events;

namespace YourProject.Domain;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]

// 注文イベント
[JsonSerializable(typeof(EventDocument<OrderCreated>))]
[JsonSerializable(typeof(OrderCreated))]
[JsonSerializable(typeof(EventDocument<OrderPaid>))]
[JsonSerializable(typeof(OrderPaid))]
[JsonSerializable(typeof(EventDocument<OrderCancelled>))]
[JsonSerializable(typeof(OrderCancelled))]
[JsonSerializable(typeof(EventDocument<OrderShipped>))]
[JsonSerializable(typeof(OrderShipped))]

// 商品イベント
[JsonSerializable(typeof(EventDocument<ProductCreated>))]
[JsonSerializable(typeof(ProductCreated))]
[JsonSerializable(typeof(EventDocument<ProductUpdated>))]
[JsonSerializable(typeof(ProductUpdated))]
[JsonSerializable(typeof(EventDocument<ProductPriceChanged>))]
[JsonSerializable(typeof(ProductPriceChanged))]
[JsonSerializable(typeof(EventDocument<ProductDiscontinued>))]
[JsonSerializable(typeof(ProductDiscontinued))]

// ユーザーイベント
[JsonSerializable(typeof(EventDocument<UserRegistered>))]
[JsonSerializable(typeof(UserRegistered))]
[JsonSerializable(typeof(EventDocument<UserEmailVerified>))]
[JsonSerializable(typeof(UserEmailVerified))]
[JsonSerializable(typeof(EventDocument<UserPasswordChanged>))]
[JsonSerializable(typeof(UserPasswordChanged))]
[JsonSerializable(typeof(EventDocument<UserDeactivated>))]
[JsonSerializable(typeof(UserDeactivated))]

// 必要に応じて追加のイベントタイプを追加
public partial class YourProjectDomainEventsJsonContext : JsonSerializerContext
{
}
```

## 命名と構成

JSONコンテキストクラスは次のようにすべきです：
1. ドメインプロジェクトのルート名前空間に配置する
2. あなたのドメインに従って命名する、通常は `{YourProject}DomainEventsJsonContext`
3. ドメインで使用されるすべてのイベントタイプを含める

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
        // 実装はドメインモデルに基づいて生成されます
        ...

    // シリアライズチェックに使用
    public static SekibanDomainTypes Generate() => 
        Generate(new JsonSerializerOptions());
}
```

### ソース生成に関する重要なポイント

1. **命名規則**:
   - 生成されたクラスは `[ProjectName]DomainDomainTypes` というパターンに従います
   - 例えば、"SchoolManagement"という名前のプロジェクトは `SchoolManagementDomainDomainTypes` となります

2. **名前空間**:
   - 生成されたクラスは `[ProjectName].Generated` 名前空間に配置されます
   - 例えば、`SchoolManagement.Domain.Generated`

3. **アプリケーションでの使用法**:
   ```csharp
   // Program.csで
   builder.Services.AddSingleton(
       YourProjectDomainDomainTypes.Generate(
           YourProjectDomainEventsJsonContext.Default.Options));
   ```

4. **テストでの使用法**:
   ```csharp
   // テストクラスで
   protected override SekibanDomainTypes GetDomainTypes() => 
       YourProjectDomainDomainTypes.Generate(
           YourProjectDomainEventsJsonContext.Default.Options);
   ```

5. **テストに必要なインポート**:
   ```csharp
   using YourProject.Domain;
   using YourProject.Domain.Generated; // 生成されたタイプを含む
   using Sekiban.Pure;
   using Sekiban.Pure.xUnit;
   ```

### ソース生成のトラブルシューティング

1. **生成されたタイプが見つからない**:
   - テストを実行する前にプロジェクトが正常にビルドされていることを確認する
   - すべてのドメインタイプに必要な属性があることを確認する
   - ソース生成に関するビルド警告を確認する

2. **名前空間エラー**:
   - 正しいGenerated名前空間をインポートしていることを確認する
   - 名前空間はソースファイルでは表示されず、コンパイルされたアセンブリでのみ表示される

3. **タイプが見つからないエラー**:
   - 正しい命名規則を使用していることを確認する
   - クラス名のタイプミスを確認する

## Orleansシリアライゼーション

Sekibanは分散メッセージングとストレージのためにOrleansのシリアライゼーションシステムを活用しています。Orleansはグレイン間で渡されるすべての型がシリアライズ可能である必要があります。

### GenerateSerializer属性の使用

Orleansでシリアライズする必要があるすべての型には、`[GenerateSerializer]`属性を追加します。これには以下が含まれます：

1. コマンド
2. イベント
3. 集約（ペイロードレコード）
4. クエリ結果
5. クエリパラメータ

```csharp
// コマンドの例
[GenerateSerializer]
public record CreateOrderCommand(...) : ICommandWithHandler<...>
{
    // 実装
}

// イベントの例
[GenerateSerializer]
public record OrderCreated(...) : IEventPayload
{
    // 実装
}

// 集約ペイロードの例
[GenerateSerializer]
public record Order(...) : IAggregatePayload
{
    // 実装
}

// クエリ結果の例
[GenerateSerializer]
public record OrderSummary(...)
{
    // 実装
}
```

### フィールドとプロパティのシリアライゼーション

レコード型以外の場合、Orleansではシリアライズすべきフィールドを`[Id]`属性を使用して指定する必要があります：

```csharp
public class ComplexType
{
    [Id(0)]
    public string PropertyA { get; set; } = null!;

    [Id(1)]
    public int PropertyB { get; set; }

    [Id(2)]
    public List<string> Items { get; set; } = new();
}
```

単純なレコードの場合、プロパティは明示的な`[Id]`属性を必要とせずに自動的にシリアライズされます。

### カスタムOrleansシリアライザー

Orleansのシリアライゼーションをカスタマイズする必要がある場合、Sekibanはデフォルトの System.Text.Jsonの代わりにNewtonsoft.Jsonを使用する方法を提供します：

```csharp
// Newtonsoft.Jsonを使用したカスタムOrleansシリアライザーの例
public class NewtonsoftJsonSekibanOrleansSerializer : IGrainStorageSerializer
{
    private readonly JsonSerializerSettings _settings;

    public NewtonsoftJsonSekibanOrleansSerializer() =>
        _settings = new JsonSerializerSettings
        {
            // System.Text.JsonのIncludeFields = trueと同様
            ContractResolver = new DefaultContractResolver()
        };
    public BinaryData Serialize<T>(T input)
    {
        var json = JsonConvert.SerializeObject(input, _settings);
        return BinaryData.FromString(json);
    }

    public T Deserialize<T>(BinaryData input)
    {
        var json = input.ToString();
        return JsonConvert.DeserializeObject<T>(json, _settings);
    }
}
```

### カスタムシリアライザーの登録

```csharp
// Program.csで
builder.UseOrleans(siloBuilder =>
{
    siloBuilder.AddMemoryGrainStorage("PubSubStore");
    
    // カスタムシリアライザーを使用
    siloBuilder.Services.AddSingleton<IGrainStorageSerializer, NewtonsoftJsonSekibanOrleansSerializer>();
    
    // その他のOrleans設定
});
```

## ベストプラクティス

1. **DTOにレコードを使用する**：不変性とシリアライゼーションの簡素化を確保するために、すべてのData Transfer Objects（DTOs）にC#レコードを使用する
2. **すべてのイベントタイプを登録する**：すべてのイベントタイプがJSONコンテキストに登録されていることを確認する
3. **適切な名前空間の整理**：タイプを適切な名前空間に保持する
4. **循環依存を避ける**：型階層での循環依存を避ける
5. **シリアライゼーションをテストする**：タイプが適切にシリアライズおよび逆シリアライズできることを検証するために単体テストを使用する
6. **バージョニング戦略**：後方互換性を確保するためにイベントのバージョニングを計画する
7. **イベントをシンプルに保つ**：シリアライゼーションの問題を避けるためにイベントペイロードをシンプルかつ焦点を絞ったものにする