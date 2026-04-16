# マテリアライズドビュー基礎

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
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)
> - [コールドイベントとキャッチアップ](19_cold_events.md)
> - [マテリアライズドビュー基礎](20_materialized_view.md) (現在位置)

マテリアライズドビューは、DCB のイベントストリームから更新されるデータベース上のリードモデルです。Orleans の
メモリ状態だけに依存するのではなく、順序付けられたイベントを SQL テーブルへ反映し、そのテーブルを直接
クエリできるようにします。

## 何のために使うのか

マルチプロジェクションが DCB の標準的な読み取りモデルですが、次のような要件ではマテリアライズドビューが
向いています。

- 大きな一覧に対する SQL のページング・絞り込み・並び替え
- ダッシュボードや BI ツールなど、外部からのテーブル参照
- イベントストアとは別に最適化したインデックスを持つ読み取り用テーブル
- Grain の非アクティブ化とは独立して保持される DB リードモデル

アプリケーション内部だけで完結するならマルチプロジェクション、DB として公開したいならマテリアライズドビュー、
という使い分けが基本です。

## ランタイム構成

現在の実装は 3 つのパッケージに分かれています。

- `Sekiban.Dcb.MaterializedView`
  共通契約。`IMaterializedViewProjector`、`IMvInitContext`、`IMvApplyContext`、`MvRegistryEntry`、
  キャッチアップワーカーなどを含みます。
- `Sekiban.Dcb.MaterializedView.Postgres`
  PostgreSQL 向けのテーブル登録、レジストリ保存、イベント適用、行アクセス実装です。
- `Sekiban.Dcb.MaterializedView.Orleans`
  Orleans Grain による起動、状態確認、`IMvOrleansQueryAccessor` を提供します。

イベントの正本はあくまで DCB のイベントストアであり、マテリアライズドビューはその下流投影です。

## 全体の流れ

1. DCB のコマンドがイベントストアへイベントを書き込む
2. マテリアライズドビュー runtime がイベントストアから順序付きイベントを読む
3. プロジェクターがイベントを SQL 文へ変換する
4. レジストリが現在位置とアクティブバージョンを保持する
5. Orleans が stream 配信、バッファ、refresh を制御する
6. アプリケーションが DB テーブルをクエリする

つまり、整合性の中心は SQL テーブルではなく、順序付きイベント適用です。

## 登録方法

Orleans ホストでの基本的な登録例です。

```csharp
builder.Services.AddSekibanDcbMaterializedView(options =>
{
    options.BatchSize = 100;
    options.PollInterval = TimeSpan.FromSeconds(1);
});

builder.Services.AddMaterializedView<WeatherForecastMvV1>();

builder.Services.AddSekibanDcbMaterializedViewPostgres(
    builder.Configuration,
    connectionStringName: "DcbMaterializedViewPostgres",
    registerHostedWorker: false);

builder.Services.AddSekibanDcbMaterializedViewOrleans();
```

出典: `internalUsages/DcbOrleans.WithoutResult.ApiService/Program.cs`

役割は次の通りです。

- `AddSekibanDcbMaterializedView`
  共通オプション登録
- `AddMaterializedView<TView>`
  1 つのプロジェクター登録
- `AddSekibanDcbMaterializedViewPostgres`
  PostgreSQL 向けレジストリと executor の登録
- `AddSekibanDcbMaterializedViewOrleans`
  Orleans 側の起動と query accessor の登録

## プロジェクターの書き方

マテリアライズドビューのプロジェクターは `IMaterializedViewProjector` を実装します。

```csharp
public sealed class WeatherForecastMvV1 : IMaterializedViewProjector
{
    public string ViewName => "WeatherForecast";
    public int ViewVersion => 1;

    public MvTable Forecasts { get; private set; } = default!;

    public async Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
    {
        Forecasts = ctx.RegisterTable("forecasts");
        await ctx.ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS {Forecasts.PhysicalName} (
                forecast_id UUID PRIMARY KEY,
                location TEXT NOT NULL,
                forecast_date DATE NOT NULL,
                temperature_c INT NOT NULL,
                summary TEXT NULL,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                _last_sortable_unique_id TEXT NOT NULL,
                _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """, cancellationToken: cancellationToken);
    }

    public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
        Event ev,
        IMvApplyContext ctx,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MvSqlStatement>>([]);
}
```

出典: `internalUsages/Dcb.Domain.WithoutResult/MaterializedViews/WeatherForecastMvV1.cs`

責務は 2 つです。

- `InitializeAsync`
  論理テーブル登録と `CREATE TABLE` / `CREATE INDEX` 発行
- `ApplyToViewAsync`
  1 イベントを 1 個以上の SQL 文へ変換

## 順序保証と冪等性

マテリアライズドビューはリプレイ可能である必要があります。基本パターンは次の通りです。

- 各行に `_last_sortable_unique_id` を持つ
- 受信イベントの sortable id が新しいときだけ更新する
- 順序の正本は event store とする

例:

```sql
UPDATE some_table
SET value = @Value,
    _last_sortable_unique_id = @SortableUniqueId
WHERE id = @Id
  AND _last_sortable_unique_id < @SortableUniqueId;
```

これにより、catch-up と stream 適用が最終的に同じ状態へ収束できます。

## レジストリで管理するもの

ランタイムは logical table ごとに次の運用情報を保持します。

- service id
- view name / active version
- logical table 名と物理 table 名
- current position / last sortable unique id
- 適用済みイベント数
- stream 側 / catch-up 側の最終適用 sortable id

用途は次の通りです。

- 現在有効な物理テーブルの解決
- 運用向け status 表示
- catch-up 中か ready か active かの判断

## テーブルのクエリ方法

物理テーブル名をアプリ側で決め打ちしないでください。`IMvOrleansQueryAccessor` を使って解決します。

```csharp
var context = await mvQueryAccessor.GetAsync(projector);
var forecastEntry = context.GetRequiredTable("forecasts");

await using var connection = new NpgsqlConnection(context.ConnectionString);
await connection.OpenAsync();

var rows = await connection.QueryAsync<WeatherForecastMvRow>(
    $"SELECT * FROM {forecastEntry.PhysicalTable} WHERE is_deleted = FALSE");
```

query context から取得できるもの:

- `DatabaseType`
- `ConnectionString`
- `Entries`
- `Grain`

`Grain` は status 確認や、ある `SortableUniqueId` が受信済みかどうかの待機にも使えます。

## マルチプロジェクションとの違い

| 観点 | マルチプロジェクション | マテリアライズドビュー |
| --- | --- | --- |
| 保存先 | Orleans Grain 状態 | SQL テーブル |
| 読み取り経路 | `ISekibanExecutor.QueryAsync` | SQL / Dapper / DB アクセス |
| 向いている用途 | アプリ内部の読み取りモデル | 一覧、レポート、外部参照 |
| 最新性制御 | `WaitForSortableUniqueId` | Grain status + SQL 読み取り |
| スキーマ管理 | 投影 payload | 明示的な table DDL |

両者は排他的ではなく、同じサービスで併用できます。

## 現在のスコープ

現時点の実装範囲:

- DB backend: PostgreSQL
- 実行ホスト: Orleans
- イベントの正本: 既存の DCB event store

サンプル実装 `internalUsages/DcbOrleans.WithoutResult.ApiService` では、

- イベントストアは Postgres
- マテリアライズドビュー用テーブルも別 Postgres 接続で管理
- Orleans が status、buffering、refresh を制御

という構成になっています。

## 実務上の指針

- 最初は 1 projector / 1 logical table から始める
- 行スキーマは明示的かつ単純に保つ
- `_last_sortable_unique_id` は必ず持つ
- 公開するクエリ形に合わせて index を張る
- テーブル定義や投影ロジック変更時は `ViewVersion` を上げる
- 正本は event store であり、マテリアライズドビューは再構築可能にしておく

## 関連資料

- [マルチプロジェクション](04_multiple_aggregate_projector.md)
- [クエリ](05_query.md)
- [ストレージプロバイダー](11_storage_providers.md)
- `internalUsages/Dcb.Domain.WithoutResult/MaterializedViews/WeatherForecastMvV1.cs`
- `internalUsages/DcbOrleans.WithoutResult.ApiService/Program.cs`
