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

SEK-G23 からは、`IExecutedUserProvider` を DI に登録することもできます。コマンド経路ではコマンドごとに 1 回だけ評価され、そのコマンドが生成するすべてのイベントの `EventMetadata.ExecutedUser` に書き込まれます。プロバイダーが未登録、または `null`/空文字を返した場合は `"GeneralSekibanExecutor"` にフォールバックします。シリアライズ/WASM コミット経路は常に `"SerializedSekibanExecutor"` を使用します。

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IExecutedUserProvider>(sp =>
    new HttpContextExecutedUserProvider(sp.GetRequiredService<IHttpContextAccessor>()));
```

> **ライフタイムの指針。** executor はプロバイダーをキャプチャします。scoped または transient のプロバイダーを使う場合は、executor も scoped または transient で登録してください。アンビエント HTTP コンテキスト方式ではプロバイダーが singleton なので、executor も singleton にできます。

## ストリーム連携

`IEventPublisher` を登録すると Orleans ストリームや外部キューにイベントを配信できます。
`OrleansEventPublisher` (`src/Sekiban.Dcb.Orleans/OrleansEventPublisher.cs`) がその例です。

## オプションの executor サイズゲート

`Sekiban.Dcb.SizeGates` の `ExecutorSizeGateOptions` を使うと、executor のサイズ検査を明示的に有効化できます。
`AddSekibanDcbExecutorSizeGate` で登録し、加算された executor コンストラクターへ渡してください。既存の
コンストラクターと DI 登録はゲートなしのままです。検査は最終的に準備されたイベント全体に対して 1 回だけ行われ、
型付きコマンド、シリアライズ済みバッチ、expected-tag-position バッチ、型付き conditional append、
シリアライズ済み conditional append の 5 つの永続化経路を対象にします。イベント・タグ・head の書き込み前に拒否し、
生成済みのイベント ID を保持し、ハンドラーを 2 回呼び出すことはありません。

組み込みの `LogicalSerializedEventUtf8` は、シリアライズ済み payload、イベント型、最終的な sortable/id、metadata、
タグを含む規約化 UTF-8 エンベロープを測定します。storage-item と destination のポリシーには、正確なバイト数または
保守的な認証済み上限を返す明示的なプロバイダー測定機能が必要です。Orleans の destination 検査はサービス単位の
resolver 計画を取得し、同じ計画を publish に使うため、resolver の変更で判定を回避できません。Azure Queue の
wire envelope/batch サイズ、retry、トランスポート固有の保証はこの core 契約の対象外です。

strict ポリシーは、永続化前に型付き capability または limit 例外として失敗します。non-strict ポリシーは、測定不能時に
成功結果の metadata へ `ExecutorSizeDiagnostic` を明示的に記録しますが、検証済みとは扱いません。ポリシーは executor
全体に適用されるため、`maxBytesPerOperation` はバッチ全体を合計します。既存の public constructor/interface と直接 API は
互換性を保ち、サイズゲートは加算的な opt-in です。過去イベントの自動修復や publisher のフォールバック経路は追加しません。
