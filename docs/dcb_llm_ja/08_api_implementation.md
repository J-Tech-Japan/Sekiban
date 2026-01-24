# API実装 - Minimal API で DCB を操作

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [コマンド・イベント・タグ・プロジェクター](03_aggregate_command_events.md)
> - [マルチプロジェクション](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [コマンドワークフロー](06_workflow.md)
> - [シリアライゼーションとドメイン型登録](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md) (現在位置)
> - [クライアントUI (Blazor)](09_client_api_blazor.md)
> - [Orleans構成](10_orleans_setup.md)
> - [ストレージプロバイダー](11_storage_providers.md)
> - [テスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)

サンプル API (`internalUsages/DcbOrleans.ApiService/Program.cs`) は Minimal API を採用し、
`ISekibanExecutor` を介してコマンド/クエリを実行します。

## 基本パターン

```csharp
var apiRoute = app.MapGroup("/api");

apiRoute.MapPost("/students", async (CreateStudent command, ISekibanExecutor executor) =>
{
    var result = await executor.ExecuteAsync(command);
    return result.IsSuccess
        ? Results.Ok(new
        {
            command.StudentId,
            eventId = result.GetValue().EventId,
            sortableUniqueId = result.GetValue().SortableUniqueId
        })
        : Results.BadRequest(new { error = result.GetException().Message });
});
```

カスタムハンドラーが必要なコマンドは `ExecuteAsync(command, handlerFunc)` を使用します。

## クエリエンドポイント

ページングや `waitForSortableUniqueId` をクエリ パラメーターとして受け取り、クエリレコードに渡します。

```csharp
apiRoute.MapGet("/students", async (ISekibanExecutor executor, int? pageNumber, int? pageSize, string? waitFor) =>
{
    var query = new GetStudentListQuery
    {
        PageNumber = pageNumber ?? 1,
        PageSize = pageSize ?? 20,
        WaitForSortableUniqueId = waitFor
    };
    var result = await executor.QueryAsync(query);
    return result.IsSuccess
        ? Results.Ok(result.GetValue().Items)
        : Results.BadRequest(new { error = result.GetException().Message });
});
```

タグ状態を直接取得するエンドポイントでは `new TagStateId(tag, projectorName)` を用いて
`executor.GetTagStateAsync` を呼び出します。

## 共通機能

- **ProblemDetails**: `AddProblemDetails()` を有効化し、検証エラーを 400 で返却。
- **CORS**: Blazor クライアント向けに CORS を許可。
- **OpenAPI / Scalar**: 開発環境で Swagger + Scalar ドキュメントを公開。
- **ロギング**: Azure SDK の冗長ログはフィルタリング (`builder.Logging.AddFilter(...)`)。

## エラーのマッピング

- `CommandValidationException` → 400
- 予約失敗 (`Failed to reserve tags`) → 409
- イベントストア障害 → 500

`ResultBox` の例外メッセージをメッセージとして返すことで、クライアントは再試行可否を判断できます。

## 認証/認可

テンプレートには組み込まれていないため、`RequireAuthorization` などで API 層に追加してください。
ユーザーID を `EventMetadata.ExecutedUser` に書き込むには `ISekibanExecutor` をデコレートします。

## ストリーム連携

`IEventPublisher` を登録すると Orleans ストリームや外部キューにイベントを配信できます。
`OrleansEventPublisher` (`src/Sekiban.Dcb.Orleans/OrleansEventPublisher.cs`) がその例です。
