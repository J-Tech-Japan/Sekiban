# はじめに - Sekiban DCB

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md) (現在位置)
> - [コマンド・イベント・タグ・プロジェクター](03_aggregate_command_events.md)
> - [マルチプロジェクション](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [コマンドワークフロー](06_workflow.md)
> - [シリアライゼーションとドメイン型登録](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントUI (Blazor)](09_client_api_blazor.md)
> - [Orleans構成](10_orleans_setup.md)
> - [ストレージプロバイダー](11_dapr_setup.md)
> - [テスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)

## テンプレートのインストール

DCB には Orleans + Aspire を利用したテンプレートが用意されています。

```bash
# Sekiban DCB テンプレートをインストール
dotnet new install Sekiban.Dcb.Templates

# Orleans + Aspire + API + Blazor を生成
dotnet new sekiban-dcb-orleans -n Contoso.Dcb
```

テンプレートには次が含まれます。

- Orleans サイロと Aspire AppHost
- Azure Queue ストリーム設定
- PostgreSQL (マイグレーション プロジェクト付き) と Cosmos DB 切替
- コマンド/クエリを公開する API サービス
- API を呼び出す Blazor UI
- ServiceDefaults プロジェクト (ログ/監視/Otel)

テンプレート詳細: `templates/Sekiban.Dcb.Templates/README.md`

## ソリューション構成

```
Contoso.Dcb.Domain/         // コマンド、イベント、タグ、プロジェクター、クエリ
Contoso.Dcb.ApiService/     // Minimal API と DI
Contoso.Dcb.Web/            // Blazor Server UI
Contoso.Dcb.AppHost/        // Aspire オーケストレーション
Contoso.Dcb.ServiceDefaults// 共通ホスティング設定
Contoso.Dcb.Tests/          // テスト雛形
```

完全な例は `internalUsages/Dcb.Domain` を参照してください。

## ドメイン型の登録

DCB は `DcbDomainTypes` による型登録が必須です。イベント、タグ、プロジェクター、クエリなどをまとめて定義し
ます。

```csharp
// internalUsages/Dcb.Domain/DomainType.cs
public static DcbDomainTypes GetDomainTypes() =>
    DcbDomainTypes.Simple(types =>
    {
        types.EventTypes.RegisterEventType<StudentCreated>();
        types.TagProjectorTypes.RegisterProjector<StudentProjector>();
        types.TagStatePayloadTypes.RegisterPayloadType<StudentState>();
        types.TagTypes.RegisterTagGroupType<StudentTag>();
        types.MultiProjectorTypes.RegisterProjector<WeatherForecastProjection>();
        types.QueryTypes.RegisterListQuery<GetStudentListQuery>();
    });
```

テンプレートでは `builder.Services.AddSingleton(DomainType.GetDomainTypes());` が自動で設定され、API/Orleans の両方
から参照されます。

## エグゼキューターのバインド

Orleans 環境では `OrleansDcbExecutor` を登録します (`src/Sekiban.Dcb.Orleans/OrleansDcbExecutor.cs`)。

```csharp
builder.Services.AddSingleton<ISekibanExecutor, OrleansDcbExecutor>();
```

ローカル検証のみであれば `InMemorySekibanExecutor` (`src/Sekiban.Dcb/InMemory`) を利用し、サイロなしでコマンドを
実行できます。

## 最初のコマンド

`CreateStudent` の実装例 (`internalUsages/Dcb.Domain/Student/CreateStudent.cs`) に沿って作成します。

1. `ICommandWithHandler<T>` を実装し、必要なら DataAnnotations で検証。
2. `ICommandContext` でタグ状態を取得し、ドメイン制約を確認。
3. `EventOrNone.EventWithTags` でイベントとタグをまとめて返す。

```csharp
public static Task<ResultBox<EventOrNone>> HandleAsync(CreateStudent command, ICommandContext context) =>
    ResultBox.Start
        .Remap(_ => new StudentTag(command.StudentId))
        .Combine(tag => context.TagExistsAsync(tag))
        .Verify((_, exists) => exists
            ? ExceptionOrNone.FromException(new ApplicationException("Student Already Exists"))
            : ExceptionOrNone.None)
        .Conveyor((tag, _) => EventOrNone.EventWithTags(
            new StudentCreated(command.StudentId, command.Name, command.MaxClassCount),
            tag));
```

## アプリの起動

```bash
dotnet restore
dotnet run --project Contoso.Dcb.AppHost
```

Aspire ダッシュボードから Orleans サイロ、API、Blazor、データベースの状態を確認できます。

## 次のステップ

- `internalUsages/Dcb.Domain` を参考にタグ/プロジェクター/クエリを拡充
- API ガイドに従いエンドポイントを公開
- マルチプロジェクションで読み取りモデルを作成
