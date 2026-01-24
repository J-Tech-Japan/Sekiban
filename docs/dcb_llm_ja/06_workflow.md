# コマンドワークフロー - 予約と永続化

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [コマンド・イベント・タグ・プロジェクター](03_aggregate_command_events.md)
> - [マルチプロジェクション](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [コマンドワークフロー](06_workflow.md) (現在位置)
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

`GeneralSekibanExecutor` はコマンドの検証からイベント永続化までを一貫して実行します。

## 処理の流れ

1. **検証**: DataAnnotations による入力チェック (`CommandValidator`)。
2. **コンテキスト生成**: `GeneralCommandContext` がタグ状態アクセスやイベント追加を記録。
3. **ハンドラー実行**: コマンドハンドラーが `EventOrNone` を返す。
4. **タグ収集**: 返されたイベントに含まれるすべてのタグをユニーク化。
5. **予約**: `IsConsistencyTag()` が true のタグについて `MakeReservationAsync` を実行。
6. **永続化**: `IEventStore.WriteEventsAsync` でイベントとタグリンクをまとめて保存。
7. **予約確定**: 成功した予約に対して `ConfirmReservationAsync` を呼び出し、再キャッチアップを指示。
8. **イベント配信**: `IEventPublisher` が設定されていれば外部ストリームに発行。
9. **結果返却**: `ExecutionResult` にイベントIDや `SortableUniqueId` を格納。

実装は `src/Sekiban.Dcb/Actors/GeneralSekibanExecutor.cs` を参照。

## 予約メカニズム

- 非整合性タグ (`IsConsistencyTag()` が false) は予約スキップ。
- `ConsistencyTag` で `SortableUniqueId` を指定するとそのバージョンでOCCを実施。
- 取得済み状態がある場合は `TagState.LastSortedUniqueId` を使用。
- 予約は `TagConsistentActorOptions.CancellationWindowSeconds` でタイムアウト管理されます。

衝突が起こると例外を返し、成功していた予約はすべてキャンセルされます。

## 永続化

`WriteEventsAsync` はイベントと `TagWriteResult` を返します。Postgres/Cosmos いずれも `SortableUniqueId` を基準に
ソートし、高速な再取得を実現します。

## 観測ポイント

`ExecutionResult` には以下が含まれます。

- `EventId` / `SortableUniqueId`
- 書き込んだイベント数
- タグごとの書き込み結果 (`TagWriteResult`)
- 実行時間

これらをログに出力することで、衝突率や処理時間を可視化できます。

## 失敗シナリオ

- **検証エラー**: 予約前に 400 を返す。
- **予約衝突**: 409 を返してクライアントにリトライを促す。
- **永続化失敗**: 500 を返し、予約はキャンセルされる。
- **確認失敗**: 稀に発生。予約はタイムアウトまで残り、再実行で解消されるケースが多い。

## 再実行戦略

コマンドは冪等であるべきです。GUID など副作用のある値はコマンド生成時に固定し、再実行でも同じイベントが
出力されるようにします。

## 拡張ポイント

- `IEventPublisher` を実装して外部メッセージングへ転送。
- `IActorObjectAccessor` を差し替えて別アクターフレームワークへ適用。
- `ISekibanExecutor` をラップしてメトリクスやリトライポリシーを追加。

詳細なシーケンス図や状態遷移は `tasks/dcb.design/dcb.concept.md` を参照してください。
