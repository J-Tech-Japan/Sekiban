# よくある問題と解決策 - DCB

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
> - [ストレージプロバイダー](11_dapr_setup.md)
> - [テスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md) (現在位置)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)

## Failed to Reserve Tags

**症状**: `InvalidOperationException` "Failed to reserve tags"。

**原因**:
- 別コマンドが先に同じタグを書き込み、バージョンが更新された。
- 予約が残ったまま (長時間実行、アクター再起動など)。

**対処**:
- 読み取り後にリトライする場合は `ConsistencyTag.FromTagWithSortableUniqueId` を使用。
- `TagConsistentActorOptions.CancellationWindowSeconds` を調整。
- 予約メトリクスをログに出してボトルネックを特定。

## 型未登録エラー

**症状**: "Event type not registered" 等。

**対処**: `DomainType.GetDomainTypes()` にイベント/タグ/プロジェクター/クエリを登録しているか確認。

## 投影が更新されない

**症状**: `waitForSortableUniqueId` がタイムアウト、古いデータが返る。

**原因**: Orleans ストリーム切断、プロジェクションバージョン不一致。

**対処**: Orleans ダッシュボードで例外を確認し、必要ならサイロを再起動。バージョン文字列の整合性をチェック。

## Postgres 起動時の失敗

**原因**: テーブル未作成、権限不足。

**対処**: `Sekiban.Dcb.Postgres.MigrationHost` を実行。接続文字列と権限を確認。

## Cosmos RU 超過

**症状**: 429 (Request rate too large)。

**対処**: RU を増やす、`waitForSortableUniqueId` の利用頻度を減らす。

## JSON シリアライズ例外

**対処**: イベントペイロードの変更は後方互換にする。`EventMetadata.EventType` をログに出し問題のイベントを特定。

## Azure Queue ストリームの欠損

**症状**: 投影が追いつかない。

**対処**: キューの存在・権限を確認。`BatchContainerBatchSize` や `GetQueueMsgsTimerPeriod` を調整。ローカルでは Azurite 設定を確認。

## Dapr 連携

DCB の Dapr 版は未提供です。`Sekiban.Pure.Dapr` は古いランタイム向けであり、DCB では Orleans をご利用ください。
