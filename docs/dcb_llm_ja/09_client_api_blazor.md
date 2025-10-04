# クライアントUI (Blazor) - DCB API の利用

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [コマンド・イベント・タグ・プロジェクター](03_aggregate_command_events.md)
> - [マルチプロジェクション](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [コマンドワークフロー](06_workflow.md)
> - [シリアライゼーションとドメイン型登録](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントUI (Blazor)](09_client_api_blazor.md) (現在位置)
> - [Orleans構成](10_orleans_setup.md)
> - [ストレージプロバイダー](11_dapr_setup.md)
> - [テスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)

テンプレートの Blazor Server アプリ (`internalUsages/DcbOrleans.Web`) は API クライアントを通じて DCB と連携する
方法を示します。

## APIクライアント

`StudentApiClient` は `HttpClient` をラップし、ページングや `waitForSortableUniqueId` を簡単に扱えるようにします。

```csharp
public async Task<StudentState[]> GetStudentsAsync(int? pageNumber = null, int? pageSize = null, string? waitForSortableUniqueId = null)
{
    var requestUri = BuildQueryString(pageNumber, pageSize, waitForSortableUniqueId);
    return await httpClient.GetFromJsonAsync<List<StudentState>>(requestUri) ?? [];
}

public async Task<CommandResponse> CreateStudentAsync(CreateStudent command)
{
    var response = await httpClient.PostAsJsonAsync("/api/students", command);
    // 成功時はイベントIDと SortableUniqueId を返却
}
```

レスポンスには `SortableUniqueId` が含まれ、再読み込み時に最新状態まで待つことができます。

## UIパターン

- **モーダルフォーム**: `Students.razor` は `EditForm` + DataAnnotations で入力を検証。
- **ページング**: クエリのページ番号/サイズと UI を連動させています。
- **再読込**: コマンド成功後 `LoadStudents(result.SortableUniqueId)` を呼び、最新イベントが投影されるまで待機。
- **StreamRendering**: サーバーサイド相互作用を利用して低遅延で更新。

## レイアウト

`MainLayout.razor` と `NavMenu.razor` で学生/教室/登録/天気などのページを切り替えます。各ページは同じテンプレート
(クエリ呼び出し → 結果表示 → コマンド送信) を踏襲しています。

## 依存性注入

`Program.cs` で `AddHttpClient<StudentApiClient>` を登録し、Blazor のコンポーネントに `@inject` します。

## エラーハンドリング

- API からのエラーはモーダル内に表示 (`studentModel.Error`)。
- `ILogger<T>` で例外をログに記録。
- 必要に応じてトースト通知コンポーネントを追加。

## 拡張手順

1. ドメインに新しいコマンド/クエリを追加し、`DcbDomainTypes` に登録。
2. API プロジェクトでエンドポイントを実装。
3. Blazor 側で typed client メソッドを作成。
4. コンポーネントから呼び出し、UI に反映。

Blazor 以外のフロントエンドでも同じ考え方で `waitForSortableUniqueId` を扱えます。
