# Sekiban DCB 11.0.1-preview02

- バージョン: `11.0.1-preview02`。DCB 11 系の 2 つめのプレビューです。DCB ライブラリは 26 パッケージです。対象は net10.0 と net11.0 です。
- Unsafe Window マテリアライズドビュー、DynamoDB のチェックポイント書き込み、イベント配信統計の不具合を修正しました。公開 API と依存パッケージは 11.0.1-preview01 から変わっていません。
- DCB のイベントデータの移行は不要です。

## 修正

- **Unsafe Window マテリアライズドビュー: null 許容列の null 値。** 4 つのプロバイダー（PostgreSQL、SQL Server、MySQL、SQLite）すべてで、null 許容列が null の行の書き込みが失敗していました。値を `DBNull` として Dapper に渡しており、Dapper がこれを受け付けないためです。ワーカーは再試行のたびに失敗し、以降のイベントは反映されず、再起動後もビューは空のままでした。null 値は null として書き込むようになりました。
- **SQL Server: 型付きパラメーター。** `VARBINARY`、`BINARY`、`IMAGE` 列への null はバイナリ型の null としてバインドします。これまでは型なしで送られ、SQL Server が書き込みを拒否していました（エラー 257）。`DateTime` の値は、列が日付・時刻型のときに `DATETIME2` としてバインドするため、1753 年より前の日付や約 3 ms より細かい精度が保たれます。それ以外の列のバインドは変わりません。`DATETIME2`、`DATETIMEOFFSET`、`TIME` 列にすでに書き込まれた行は、ビューを再構築するまで以前の約 3 ms の丸めのままです。
- **PostgreSQL と SQL Server: safe から unsafe への修復コピー。** safe テーブルから unsafe テーブルへのコピーをデータベース内（`INSERT ... SELECT`）で行うようになり、`JSONB` や `DATE` などの値は型を保ったままコピーされます。これまでは .NET を経由していたため、コピーが失敗することがありました（PostgreSQL では `JSONB` のエラー 42804 や `DATE` 列、SQL Server では 1753 年より前の日付など）。
- **DynamoDB のチェックポイントストア。** 条件付きのチェックポイントの更新（最初の 1 回を除く）と、再構築したチェックポイントの確定が、すべて失敗していました（tombstone の書き込みは影響を受けていません）。リクエストに使っていない式属性名（`#u`）が含まれており、DynamoDB がこれを拒否するためです。それでも永続化は成功を返していたため、保存されたスナップショットは最初のもののまま進まず、永続的な再構築の義務も保存されていませんでした。条件付き書き込みは、使う名前だけを送るようになりました。
- **イベント配信統計。** 以前の内部リファクタリング以降、`RecordingMultiProjectionEventStatistics` にイベントが一切届かなくなっており、`GetEventDeliveryStatisticsAsync`（とテンプレートの `*/event-statistics` ルート）は常に 0 を返していました。記録を戻しました。Orleans のストリームから届いたときと、キャッチアップ（`AddEventsAsync` を含む）で取得したときに、重複除去やフィルターの前に数えます。組み込みの実装はスレッドセーフです。既定の `NoOpMultiProjectionEventStatistics` では、これまでどおり何も記録せず、メモリも確保しません。記録する実装は配信されたイベント ID をすべてメモリに保持するため、開発時だけ使ってください。独自の `IMultiProjectionEventStatistics` の実装も再び呼ばれるようになります。キャッチアップは grain のスケジューラーの外から呼ぶことがあるので、スレッドセーフにしてください。渡される `Event` の payload は代わりの値で、意味があるのは `Id`、`EventType`、`SortableUniqueIdValue`、`Tags`、`EventMetadata` だけです。

## 11.0.1-preview01 からの更新

- API の変更もデータの移行もありません。パッケージのバージョンを更新してください（`DATETIME2` の精度が必要なビューの再構築は上の SQL Server の項、独自の実装は統計の項を参照してください）。引き続きプレビューで、依存パッケージのバージョンは 11.0.1-preview01 と同じです。
- 同じ修正を 10.x 系の 10.25.1 でもリリースする予定です。

## テンプレート

11 系の 5 つの DCB テンプレートは `Sekiban.Dcb` `11.0.1-preview02`（各テンプレートの `SekibanDcbTemplateVersion.props` の `SekibanDcbVersion`）を参照するようになりました。11.0.1-preview01 以降、WithoutResult と Decider のテンプレートは `ResultBox` を使わず、`ResultBoxes` も参照しなくなりました。11.0.1-preview01 と同じく、テンプレートのプレビューはタグ付け・リリースしません。
