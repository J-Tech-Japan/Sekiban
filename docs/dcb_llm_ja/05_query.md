# クエリ - DCB の読み取り

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [コマンド・イベント・タグ・プロジェクター](03_aggregate_command_events.md)
> - [マルチプロジェクション](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md) (現在位置)
> - [コマンドワークフロー](06_workflow.md)
> - [シリアライゼーションとドメイン型登録](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントUI (Blazor)](09_client_api_blazor.md)
> - [Orleans構成](10_orleans_setup.md)
> - [ストレージプロバイダー](11_storage_providers.md)
> - [テスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)

DCB のクエリはマルチプロジェクショングレイン、もしくはタグ状態グレインからデータを読み取ります。
`ISekibanExecutor.QueryAsync` が Orleans クラスタとのやり取りを抽象化します。

## クエリのインターフェース

- `IQueryCommon<TResult>` : 単一結果クエリ
- `IListQueryCommon<TResult>` : ページング付きリストクエリ
- `IWaitForSortableUniqueId` : 指定したイベントが投影済みになるまで待機

クエリレコードは静的プロパティで対象プロジェクター名とバージョンを提供します。

```csharp
public record GetClassRoomListQuery(int Page = 1, int PageSize = 20)
    : IMultiProjectionListQuery<GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag>,
        GetClassRoomListQuery, ClassRoomItem>
{
    public static string ProjectionName => GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag>.MultiProjectorName;
    public static string ProjectionVersion => GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag>.MultiProjectorVersion;

    public int GetPage() => Page;
    public int GetPageSize() => PageSize;
}
// internalUsages/Dcb.Domain/Queries/GetClassRoomListQuery.cs
```

## クエリの実行

```csharp
var result = await executor.QueryAsync(new GetStudentListQuery { PageNumber = 1, PageSize = 10 });
if (result.IsSuccess)
{
    var items = result.GetValue().Items;
}
```

リストクエリは `ListQueryResult<T>` を返し、件数やページ情報も得られます。

## 最新データを待つ

`IWaitForSortableUniqueId` を実装すると、指定した `SortableUniqueId` が処理されるまでマルチプロジェクションが
追いつくのを待機します。

```csharp
public record GetWeatherForecastCountQuery(string? WaitForSortableUniqueId = null)
    : IMultiProjectionQuery<WeatherForecastProjection, GetWeatherForecastCountQuery, WeatherForecastCountResult>,
      IWaitForSortableUniqueId
{
    string? IWaitForSortableUniqueId.WaitForSortableUniqueId => WaitForSortableUniqueId;
}
```

`OrleansDcbExecutor` は `IsSortableUniqueIdReceived` をポーリングし、一定時間待っても届かなければ結果を返します
(`src/Sekiban.Dcb.Orleans/OrleansDcbExecutor.cs`)。

## タグ状態の直接取得

単一タグの状態が欲しいだけなら `GetTagStateAsync` を利用します。

```csharp
var tagId = TagStateId.From<StudentProjector>(new StudentTag(studentId));
var stateResult = await executor.GetTagStateAsync(tagId);
```

デバッグや管理ツールで便利です。

## JSON 契約

クエリ結果のシリアライゼーションは `DcbDomainTypes` で登録した `JsonSerializerOptions` に従います。API で公開する
場合は後方互換性を考慮してください。

## テスト戦略

インメモリエグゼキューター (`src/Sekiban.Dcb/InMemory`) を使用して、イベントをシードしクエリを実行するだけで
クエリの挙動を検証できます。Orleans に依存せず高速にテスト可能です。
