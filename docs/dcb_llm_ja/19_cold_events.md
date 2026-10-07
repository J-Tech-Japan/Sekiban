# コールドイベントとキャッチアップ

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
> - [コールドイベントとキャッチアップ](19_cold_events.md) (現在位置)
> - [マテリアライズドビュー基礎](20_materialized_view.md)

このドキュメントでは、現在のインターナルユースで使っているコールドイベント実装を説明します。対象は Orleans サンプルで使われているエクスポート処理、ハイブリッドリード、キャッチアップワーカー、設定項目です。

## 目的

コールドイベントは、十分に古く安定したイベントをホットなイベントストアからオブジェクトストレージ側へ退避しつつ、キャッチアップや再読込時の整合性を維持するための仕組みです。

現在の設計方針は次の 2 点です。

- 書き込みは従来どおりホットストアに対して行う
- 読み込み時だけコールドセグメントと最新のホットイベントを合成する

## 主な構成要素

主要なパッケージとクラス:

- `Sekiban.Dcb.Core/ColdEvents`
- `Sekiban.Dcb.ColdStorage/ColdEvents`
- `ColdExporter`
- `ColdExportBackgroundService`
- `HybridEventStore`
- `StorageBackedColdLeaseManager`
- `ColdCatalogReader`
- `AddSekibanDcbColdExport(...)`

インターナルユースのサンプル:

- `dcb/internalUsages/DcbOrleans.WithoutResult.ApiService`
- `dcb/internalUsages/DcbOrleans.Catchup.Functions`
- `dcb/internalUsages/DcbOrleans.AppHost`

## ストレージレイアウト

コールドイベントのデータは `serviceId` ごとに分かれます。

制御ファイル:

- `control/{serviceId}/manifest.json`
- `control/{serviceId}/checkpoint.json`
- `control/{leaseId}/lease.json`

セグメントファイル:

- `segments/{serviceId}/{fromSortableUniqueId}_{toSortableUniqueId}.jsonl`

それぞれの役割:

- `manifest.json` はエクスポート済みセグメント一覧と最新の safe 境界を持つ
- `checkpoint.json` は次回エクスポート開始位置を持つ
- `lease.json` は同一 `serviceId` への同時エクスポートを防ぐ
- セグメントは JSONL 形式で `SerializableEvent` を格納する

## エクスポート処理

現在のエクスポートは増分方式です。

1. `cold-export-{serviceId}` という lease を取得する
2. checkpoint を読み、そこ以降のホットイベントを読む
3. safe window を適用し、十分に古いイベントだけを対象にする
4. 可能なら最後のセグメントに追記し、無理なら新しいセグメントを作る
5. manifest と checkpoint を ETag ベースの楽観的排他で更新する
6. lease を解放する。失敗時は即時期限切れを試みる

運用上のポイント:

- lease 期間は長くても 2 分に制限される
- manifest 更新は競合時に最大 3 回リトライする
- safe なイベントがまだ存在しない場合は何も書かずに終了する

## ハイブリッドリード

`AddSekibanDcbColdEventHybridRead()` は登録済みの `IEventStore` と最後に登録された `IEventStoreFactory` をラップします。factory が作るストアは指定された service id に固定され、同じサービスの保持セグメント用 holder を共有します。factory の永続性と書き込み条件の descriptor は能力を引き上げず、そのまま転送します。ホットストアと factory はこの拡張メソッドより前に登録してください。後から登録された factory は装飾されません。factory が未登録でも利用できます。

読み取り挙動:

- コールドイベント無効時はホットストアだけを読む
- manifest がなければホットストアだけを読む
- `since` がコールド境界より新しければホットストアだけを読む
- それ以外では `LatestSafeSortableUniqueId` を境界に分割し、manifest の範囲順で対象のコールドセグメントを読み、その境界より後のホットイベントを連結する。返却イベントの重複除去やソートは行わない

classic マテリアライズドビューのキャッチアップ、durable subscription、factory 経由のサービス別 projection キャッチアップもコールドからホットへ読み進めます。grain の factory 分岐も hybrid store を認識し、そのコールドキャッチアップ用バッチサイズを使い、設定に応じてセグメント境界で snapshot を保存します。コールドデータがあるのは exporter が動くサービスだけです。他のサービスは manifest がなく、ホットを読みます。Sekiban はホットイベントを削除しません。

### リスト読み取りの保持セグメント

保持セグメントを充填・利用するのは `ReadAllSerializableEventsAsync` だけです。stream 経路は holder を利用も変更もしません。件数制限のあるリスト読み取りは対象セグメントを末尾まで一度パースし、必要な先頭部分を返し、残りのイベントだけを保持します。`loadedFrom` はそのセグメントから返した最後のイベントの id です。バッチが前のセグメントから始まっていても、呼び出し元の `since` は使いません。返したイベントは保持しません。

ヒットには同じ service id、null でない `since >= loadedFrom`、保持エントリの最後の id より小さい `since` が必要です。選択は元のセグメント順を維持し、ordinal 比較で `id > since` とします。同じ id の既存の動作も維持します。順不同の返却済み部分に `loadedFrom` より大きい id がある場合、残りだけではキャッシュなしの読み取りを再現できないため、その充填結果は保持しません。条件を満たすイベントが `maxCount` 件以上あれば manifest のロードとセグメントの open を省略します。足りなければ manifest をロードし、セグメントエントリ全体が一致する場合だけ保持内容を利用します。null の cursor、充填読み取りの元の cursor からの再試行、それより前の位置、別サービス、別セグメントはミスになります。高速読み取りも通常の cold-only の結果ログとバッチメタデータを設定し、セグメント数には充填時の manifest の値を使います。

内容は不変です。ヒット時に内容を切り詰めたり `loadedFrom` を進めたりしないため、近い位置にいる二つの reader が両方ヒットできます。内容はアトミックに置換され、同時のミスはそれぞれパースしても構いません。末尾までの消費、置換、充填失敗で内容を解放します。短い固定の idle timeout を、同じ holder を通る次のリスト読み取りで確認します。時刻は登録された `TimeProvider`、なければシステム時刻を使います。timer や timeout の設定項目はなく、放棄された consumer のセグメントは次のリスト読み取りまで参照され続けます。

メモリ使用量は holder あたり最大一つのパース済みセグメントです（既定 100,000 イベント、export は `SegmentMaxBytes` で制限されますが、パース後のオブジェクトには追加コストがあります）。singleton は一つ、factory は service id ごとに一つの holder を持ちます。このコストは MV と subscription のホストにも加わります。同じサービスの reader が異なるセグメントを交互に読む場合、毎回ミスし、毎回セグメント全体をパースしますが、結果は正しいままです。MV は引き続き `MvOptions.BatchSize` を使います。MV のセグメント alignment、prefetch、セグメント境界 checkpoint は追加しません。

### 失敗時の方針

セグメントを open またはパースできない場合、コールド部分全体を破棄し、元の `since` からホットを読みます。充填は末尾までパースするため、要求されたバッチより後の壊れた行も充填失敗となり、ホットへフォールバックし、何も保持しません。失敗した読み取りで MV の checkpoint は進みません。manifest のストレージ読み取り失敗は manifest なしとして扱いますが、manifest をデシリアライズできない場合は例外となります。これは MV と durable subscription にも適用されます。manifest の欠落範囲や、パースには成功しても短いセグメントの検出は実装していません。

## 設定

コールドイベントは主に 2 系統の設定を使います。

`Sekiban:ColdEvent` は機能本体の設定です。

```json
{
  "Sekiban": {
    "ColdEvent": {
      "Enabled": true,
      "PullInterval": "00:30:00",
      "ExportCycleBudget": "00:03:00",
      "RunOnStartup": true,
      "SafeWindow": "00:02:00",
      "SegmentMaxEvents": 30000,
      "ExportMaxEventsPerRun": 30000,
      "SegmentMaxBytes": 536870912,
      "Storage": {
        "Provider": "azureblob",
        "Format": "jsonl",
        "AzureBlobClientName": "MultiProjectionOffload",
        "AzureContainerName": "multiprojection-cold-events"
      }
    }
  }
}
```

`AddSekibanDcbColdExport(...)` は従来の互換キーも吸収します。

```json
{
  "ColdExport": {
    "Interval": "00:05:00",
    "CycleBudget": "00:03:00"
  }
}
```

ストレージ設定の主な項目:

- `Provider`: `local` または `azureblob`
- `Format`: `jsonl`、`sqlite`、`duckdb`
- `BasePath`: ローカル保存ルート。既定値は `.cold-events`
- `Type`: 旧来の一体型指定。`jsonl`、`sqlite`、`duckdb`、`azureblob`

## インターナルユースでの組み込み

### API サービス

現在の内部 API サンプルは明示的にコールドイベントを組み込んでいます。

```csharp
builder.Services.AddSekibanDcbColdEventDefaults();

if (builder.Configuration.GetSection("Sekiban:ColdEvent").GetValue<bool>("Enabled"))
{
    var coldConfig = builder.Configuration.GetSection("Sekiban:ColdEvent");
    var storageOptions = coldConfig.GetSection("Storage").Get<ColdStorageOptions>() ?? new ColdStorageOptions();
    var storageRoot = ColdObjectStorageFactory.ResolveStorageRoot(storageOptions, Directory.GetCurrentDirectory());

    builder.Services.AddSingleton(storageOptions);
    builder.Services.AddSingleton<IColdObjectStorage>(sp =>
        ColdObjectStorageFactory.Create(storageOptions, storageRoot, sp));
    builder.Services.AddSingleton<IColdLeaseManager, StorageBackedColdLeaseManager>();
    builder.Services.AddSekibanDcbColdEvents(options => coldConfig.Bind(options));
    builder.Services.AddSekibanDcbColdEventHybridRead();
}
```

この構成により次が有効になります。

- バックグラウンドのコールドエクスポート
- 手動エクスポート API
- 進捗確認 API
- カタログ確認 API
- コールドとホットをまたぐハイブリッドリード

### Catchup ワーカー

専用ワーカーは新しい共通拡張でより簡潔に構成しています。

```csharp
builder.Services.AddSekibanDcbPostgresWithAspire();
builder.Services.AddSekibanDcbColdExport(
    builder.Configuration,
    builder.Environment.ContentRootPath);
```

これにより、メイン API プロセスから分離したキャッチアップ/エクスポート専用ワーカーを少ない設定で起動できます。

### AppHost での設定例

現在の内部 Aspire AppHost では、次のような環境変数を与えています。

- `Sekiban:ColdEvent:Enabled=true`
- `Sekiban:ColdEvent:Storage:Provider=azureblob`
- `Sekiban:ColdEvent:Storage:Format=jsonl`
- `Sekiban:ColdEvent:Storage:AzureBlobClientName=MultiProjectionOffload`
- `Sekiban:ColdEvent:Storage:AzureContainerName=multiprojection-cold-events`
- `ColdExport:Interval=00:05:00`
- `ColdExport:CycleBudget=00:03:00`

## インターナル API

内部 API サービスでは次のコールドイベント用エンドポイントを公開しています。

- `GET /api/cold/status`
- `GET /api/cold/progress`
- `GET /api/cold/catalog`
- `POST /api/cold/export`
- `POST /api/cold/export-now`

これらは現時点では内部の診断・運用用途を想定しており、公開 API として安定保証する前提ではありません。

## 対応ストレージ

現在のコールドストレージ実装:

- ローカル JSONL
- ローカル SQLite
- ローカル DuckDB
- Azure Blob 上の JSONL
- Azure Blob 上の SQLite
- Azure Blob 上の DuckDB

切り替えロジックは `ColdObjectStorageFactory` に集約されています。

## 運用メモ

- `serviceId` は安定させること。manifest、checkpoint、segments はすべてこれで分離される
- `SafeWindow` は短すぎると、まだ扱いが安定していないイベントまで退避してしまう
- Azure Blob 利用時は `AzureBlobClientName` に対応する接続文字列が必要
- lease は時間ベースで失効するため、一時的な異常後も自動回復しやすい
- segment の open・パース失敗は元の cursor からホットへフォールバックする。manifest のデシリアライズ失敗は例外となる（上記の失敗時の方針を参照）

## 関連資料

- [ストレージプロバイダー](11_storage_providers.md)
- [Orleans構成](10_orleans_setup.md)
- [API実装](08_api_implementation.md)
- [マテリアライズドビュー基礎](20_materialized_view.md)
