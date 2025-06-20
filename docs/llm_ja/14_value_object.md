# Value Object - Sekiban イベントソーシング

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
> - [一般的な問題と解決策](12_common_issues.md)
> - [ResultBox](13_result_box.md)
> - [Value Object](14_value_object.md) (現在のページ)

## Value Object

Value Objectは、ドメイン駆動設計（DDD）における重要な概念で、等価性がアイデンティティではなく値に基づくオブジェクトです。Sekibanでは、強い型付けと自動バリデーションを提供するためにValue Objectを活用できます。

## 基本的な実装方針

SekibanでのValue Objectは、以下の原則に従って実装してください：

### 1. レコード型の使用

C#のレコード型を使用してValue Objectを定義します：

```csharp
using System.ComponentModel.DataAnnotations;

namespace YourDomain.ValueObjects;

[GenerateSerializer]
public record TemperatureCelsius([property:Range(-273.15, 1000000.0, ErrorMessage = "Temperature cannot be below absolute zero (-273.15°C)")] double Value)
{
    /// <summary>
    /// 摂氏から華氏に変換します
    /// </summary>
    public double GetFahrenheit() => Value * 9 / 5 + 32;
}
```

### 2. 属性ベースのバリデーション

Value Objectのバリデーションには、必ず`System.ComponentModel.DataAnnotations`の属性を使用してください：

```csharp
[GenerateSerializer]
public record Email([property:EmailAddress(ErrorMessage = "Invalid email format")] string Value);

[GenerateSerializer]
public record Age([property:Range(0, 150, ErrorMessage = "Age must be between 0 and 150")] int Value);

[GenerateSerializer]
public record ProductName([property:Required, property:StringLength(100, MinimumLength = 1, ErrorMessage = "Product name must be between 1 and 100 characters")] string Value);
```

### 3. 重要な制約事項

#### ❌ コンストラクタでのバリデーション禁止

コンストラクタ内でthrowしてはいけません：

```csharp
// ❌ やってはいけない例
[GenerateSerializer]
public record Price(decimal Value)
{
    public Price(decimal Value) : this()
    {
        if (Value < 0)
            throw new ArgumentException("Price cannot be negative"); // これはNG!
    }
}
```

#### ❌ 静的プロパティでのバリデーション禁止

静的プロパティやメソッドでのバリデーションも避けてください：

```csharp
// ❌ やってはいけない例
[GenerateSerializer]
public record UserId(string Value)
{
    public static UserId Create(string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("UserId cannot be null or empty"); // これもNG!
        
        return new UserId(value);
    }
}
```

### 4. なぜ属性ベースのバリデーションが必要なのか

#### イベントリプレイの問題

Sekibanのイベントソーシングでは、過去のイベントからアグリゲートを再構築する際にイベントリプレイが発生します。この時、コンストラクタやスタティックメソッドでバリデーションを行っていると、過去の有効だったデータが現在のバリデーションルールで失敗する可能性があります。

#### 正しい流れ

1. **コマンド入力時**: 属性ベースのバリデーションが実行される
2. **イベント生成**: バリデーション済みのデータでイベントが作成される
3. **イベントリプレイ**: バリデーションをスキップして直接オブジェクトが構築される

### 5. 複雑なValue Objectの例

より複雑なValue Objectの例：

```csharp
[GenerateSerializer]
public record Money(
    [property:Range(0, double.MaxValue, ErrorMessage = "Amount must be positive")]
    decimal Amount,
    
    [property:RegularExpression(@"^[A-Z]{3}$", ErrorMessage = "Currency must be a 3-letter code")]
    string CurrencyCode)
{
    /// <summary>
    /// 通貨を変換します
    /// </summary>
    public Money ConvertTo(string newCurrencyCode, decimal exchangeRate)
    {
        return new Money(Amount * exchangeRate, newCurrencyCode);
    }

    /// <summary>
    /// 金額を書式化して表示します
    /// </summary>
    public string FormatDisplay() => $"{Amount:F2} {CurrencyCode}";
}
```

### 6. ベストプラクティス

#### Orleanシリアライゼーション対応

必ず`[GenerateSerializer]`属性を付けてください：

```csharp
[GenerateSerializer]
public record YourValueObject(...);
```

#### 不変性の保持

Value Objectは不変であるべきです。レコード型を使用することで自動的に不変性が保証されます。

#### 意味のあるメソッドの追加

Value Objectには、そのドメインに意味のあるメソッドを追加できます：

```csharp
[GenerateSerializer]
public record Distance([property:Range(0, double.MaxValue)] double Meters)
{
    public Distance AddMeters(double additionalMeters) => new(Meters + additionalMeters);
    public double ToKilometers() => Meters / 1000.0;
    public double ToMiles() => Meters * 0.000621371;
}
```

## まとめ

SekibanでのValue Object実装では：

- ✅ レコード型を使用する
- ✅ 属性ベースのバリデーションを使用する
- ✅ `[GenerateSerializer]`を付ける
- ✅ 意味のあるメソッドを追加する
- ❌ コンストラクタでthrowしない
- ❌ 静的プロパティでバリデーションしない

これらの原則に従うことで、イベントソーシングと完全に互換性のある安全なValue Objectを作成できます。

## イベントとコマンドでのValue Object使用

Value Objectは、イベントやコマンドの永続化データとしても使用できます。これにより、ドメインの概念をより明確に表現し、型安全性を向上させることができます。

### イベントでのValue Object使用例

```csharp
[GenerateSerializer]
public record ProductPriceChanged(
    ProductId ProductId,
    Money OldPrice,
    Money NewPrice,
    DateTime ChangedAt) : IEvent;

[GenerateSerializer]
public record CustomerRegistered(
    CustomerId CustomerId,
    Email Email,
    CustomerName Name,
    Address ShippingAddress) : IEvent;
```

### コマンドでのValue Object使用例

```csharp
[GenerateSerializer]
public record ChangeProductPrice(
    ProductId ProductId,
    Money NewPrice) : ICommandWithHandler<ChangeProductPrice, ProductProjector>;

[GenerateSerializer]
public record RegisterCustomer(
    Email Email,
    CustomerName Name,
    Address ShippingAddress) : ICommandWithHandler<RegisterCustomer, CustomerProjector>;
```

### JSONシリアライゼーションの設定

Value Objectをイベントやコマンドで使用する場合、プロジェクトの`JsonSerializerContext`にも追加する必要があります。

例：`YourDomainEventsJsonContext.cs`

```csharp
using System.Text.Json.Serialization;
using YourDomain.ValueObjects;
using YourDomain.Events;
using YourDomain.Commands;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace YourDomain;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]
// ... 既存のSekiban型 ...

// Value Objectsを追加
[JsonSerializable(typeof(Money))]
[JsonSerializable(typeof(Email))]
[JsonSerializable(typeof(CustomerName))]
[JsonSerializable(typeof(Address))]
[JsonSerializable(typeof(ProductId))]
[JsonSerializable(typeof(CustomerId))]

// Value Objectsを使用するイベント
[JsonSerializable(typeof(EventDocument<ProductPriceChanged>))]
[JsonSerializable(typeof(ProductPriceChanged))]
[JsonSerializable(typeof(EventDocument<CustomerRegistered>))]
[JsonSerializable(typeof(CustomerRegistered))]

// Value Objectsを使用するアグリゲート
[JsonSerializable(typeof(Product))]
[JsonSerializable(typeof(Customer))]
public partial class YourDomainEventsJsonContext : JsonSerializerContext
{
}
```

この設定により、Value ObjectがJSONとOrleansシリアライゼーションの両方で正しく処理されます。
