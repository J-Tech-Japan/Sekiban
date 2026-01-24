# マルチプロジェクション - 合成リードモデル

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [コマンド・イベント・タグ・プロジェクター](03_aggregate_command_events.md)
> - [マルチプロジェクション](04_multiple_aggregate_projector.md) (現在位置)
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
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)

タグプロジェクターがタグ単位の状態を構築するのに対し、マルチプロジェクションは複数タグを組み合わせた
読み取りモデルを生成します。Orleans では各マルチプロジェクションが専用の Grain で動作し、大きな状態は
Azure Blob Storage にスナップショットとして退避できます。

## 基本構造

`IMultiProjector<T>` を実装して、イベントとタグ状態を引数にプロジェクションを更新します。

```csharp
public class WeatherForecastProjection : IMultiProjector<WeatherForecastProjection>
{
    public static string MultiProjectorName => "WeatherForecast";
    public static string MultiProjectorVersion => "1.0.0";

    public static MultiProjectionState Project(
        MultiProjectionState current,
        Event currentEvent,
        IReadOnlyDictionary<ITag, TagState> tagStates)
    {
        // イベントと関連タグ状態を元にリードモデルを更新
    }
}
// internalUsages/Dcb.Domain/Projections/WeatherForecastProjection.cs
```

`GenericTagMultiProjector<TProjector, TTag>` のようなジェネリック実装を使うと、タグ一覧をそのままリスト表示する
投影を簡単に作れます (`internalUsages/Dcb.Domain/DomainType.cs`)。

## 状態のライフサイクル

1. Orleans ストリーム経由でイベントを受信
2. 対象タグの最新状態を `TagStateGrain` から取得
3. プロジェクターで状態を更新
4. 必要に応じて `IBlobStorageSnapshotAccessor` を使い Blob Storage にスナップショットを保存

実装詳細は `src/Sekiban.Dcb.Orleans/Grains/MultiProjectionGrain.cs` を参照してください。

## スナップショット退避

`Sekiban.Dcb.BlobStorage.AzureStorage` を利用すると大規模な状態を Blob Storage に退避できます。

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
    new AzureBlobStorageSnapshotAccessor(
        sp.GetRequiredKeyedService<BlobServiceClient>("MultiProjectionOffload"),
        "multiprojection-snapshots"));
```

`MultiProjectionGrain` がアクセサを検出すると、定期的にスナップショットを保存しメモリ使用量を抑えます。

## 整合性のポイント

- イベントはグローバル順序で届くため、`IWaitForSortableUniqueId` を活用すると最新データを保証できます。
- プロジェクターは純粋関数（副作用なし）である必要があります。
- バージョンを更新したら `MultiProjectorVersion` を必ず変更し、リビルドを促してください。

## 代表的な用途

- ダッシュボードの集計
- Blazor UI 用の一覧ビュー
- 複数タグを跨ぐ統計情報やランキング

例: `internalUsages/Dcb.Domain/Student/StudentSummaries.cs` は複数タグから学生サマリーを組み立てています。
