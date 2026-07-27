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
> - [マテリアライズドビュー基礎](20_materialized_view.md)

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

## マルチプロジェクションとマテリアライズドビューの違い

Sekiban には現在、2 種類の読み取りモデルがあります。

- **マルチプロジェクション**: Orleans Grain 内に保持されるメモリ状態。`ISekibanExecutor.QueryAsync` と自然に接続されます。
- **マテリアライズドビュー**: 同じイベントストリームから更新される DB テーブル。SQL の一覧取得、レポート、外部参照に向きます。

Sekiban 内部だけで完結する読み取りならマルチプロジェクション、リレーショナル DB として見せたいなら
マテリアライズドビューが適しています。詳細は [マテリアライズドビュー基礎](20_materialized_view.md) を参照してください。

## デュアルステートの収束とセーフウィンドウ昇格 (SEK-G18)

マルチプロジェクションは 2 つの状態を保持します。**safe** 状態(セーフウィンドウより古い
イベントをグローバルな `SortableUniqueId` 順で反映)と、**served/unsafe** 状態(クエリが返す値)です。

- **served 状態は到着順ではなく再構成される。** セーフウィンドウ昇格のたびに、served 状態は
  `safe ベースライン + まだバッファ中のイベントをグローバル SortableUniqueId 順で再適用` として
  導出し、原子的に公開します。順序が入れ替わって到着した 2 イベント(例: インスタンス間の重複
  create)でも、全インスタンスが同じ結果に収束します。first-event-wins プロジェクタでは、ローカル
  到着順に関係なくグローバルに最も早いイベントが勝ちます。
- **`IsSafeState` は真実を表す。** served 状態が safe 状態と同一に公開された場合(バッファが空で
  再構築保留なし)にのみ `true` です。タイムスタンプ比較のみで決めることはありません。`IsSafeState=true`
  を返すクエリは、再構成済みのグローバル順の値であることが保証されます。
- **順序違反時の再構築(fail-closed)。** 保持している safe ヘッドに対してイベントがグローバル順から
  外れて safe に昇格した場合、増分(圧縮済みベースライン)経路では並べ替えできません。その場合は
  **権威イベントストアから初期状態を起点にした完全な順序付き再構築**を行います。再構築中は
  すべての state/scalar/list クエリが再構築バリアを待ち、再構築後のペイロードで応答するか fail-closed で
  失敗します。古い値で success を返すことはありません。G14 フォルト経路は再構築自体の失敗のために
  予約されています。

### チェックポイント復元の厳密性 (SEK-G18 / #1086)

- **キャッチアップ開始位置は権威的。** チェックポイント復元後、キャッチアップはチェックポイント
  レコードの `LastSortableUniqueId` から、その位置を**排他的**に読み取って開始します。id が
  チェックポイント位置と等しいイベントは復元済みペイロードに既に反映されているため再読み込みされず、
  二重カウントや再適用は起きません。(すべてのイベントストア — Postgres/SQLite/Cosmos/DynamoDB、
  インメモリ、Hybrid の cold→hot 引き継ぎ — は厳密な `SortableUniqueId > since` フィルタを使用します。)
- **`EventsProcessed` は永続的な safe チェックポイント数**で、整合性シグナルとして使用します。復元は
  これをベースラインとし、新規イベントゼロの再起動では同一の payload/position/threshold/count を復元します。
