# バリューオブジェクト - 共有概念のモデリング

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [コマンド・イベント・タグ・プロジェクター](03_aggregate_command_events.md)
> - [マルチプロジェクション](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [コマンドワークフロー](06_workflow.md)
> - [シリアライゼーションとドメイン型登録](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントUI (Blazor)](09_client_api_blazor.md)
> - [Orleans構成](10_orleans_setup.md)
> - [ストレージプロバイダー](11_storage_providers.md)
> - [テスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md) (現在位置)
> - [デプロイガイド](16_deployment.md)

DCB でも DDD と同じく、値オブジェクトを活用してドメインルールを局所化できます。

## レコードの活用

C# レコードは値等価と `with` 構文が使えるため、値オブジェクトに適しています。

```csharp
public record Capacity(int Value)
{
    public static Capacity FromInt(int value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        return new Capacity(value);
    }

    public Capacity Decrease() => this with { Value = Value - 1 };
}
```

## シリアライゼーション

値オブジェクトをイベントやクエリ結果に含める場合は、`JsonSerializerOptions` にコンバーターを登録して
`DcbDomainTypes` に渡します。

```csharp
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.Converters.Add(new CapacityJsonConverter());
var domainTypes = DcbDomainTypes.Simple(configure, options);
```

## ルールのカプセル化

コマンドハンドラーで複雑な検証を行うより、値オブジェクトのファクトリメソッドで不変条件を保証すると
理解しやすく再利用しやすくなります。

```csharp
public record EnrollmentWindow(DateOnly Start, DateOnly End)
{
    public static EnrollmentWindow Create(DateOnly start, DateOnly end)
    {
        if (start > end) throw new ArgumentException("Start must be before End");
        return new EnrollmentWindow(start, end);
    }

    public bool IsOpen(DateOnly today) => today >= Start && today <= End;
}
```

## クエリとの共有

値オブジェクトがそのまま API に露出して問題なければ共通化し、外部契約が異なる場合は DTO に変換します。

## テスト

値オブジェクト単体でファクトリメソッドや振る舞いをテストし、コマンド側では値オブジェクトが例外を投げるか
どうかだけ確認すれば十分です。

## 注意点

- 超高頻度のイベントではシリアライズ負荷を考慮し、必要十分なデータ構造にする。
- サービス間通信でプリミティブを期待される場合は変換レイヤーを用意。
