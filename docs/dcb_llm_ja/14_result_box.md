# ResultBox - 関数型パイプライン

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
> - [ResultBox](14_result_box.md) (現在位置)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)

ResultBoxes ライブラリは DCB のコマンドハンドラーで広く使われている関数型パイプラインです。

## 主要メソッド

- `ResultBox.Start` : パイプラインを開始
- `Remap` : 値を別の型に変換
- `Combine` : 非同期処理を合成 (コンテキストから状態取得など)
- `Verify` : 条件を満たさない場合に `ExceptionOrNone` を返して中断
- `Conveyor` : 最終的な `EventOrNone` などを生成

`internalUsages/Dcb.Domain/Enrollment/EnrollStudentInClassRoomHandler.cs` が代表例です。

## エラーハンドリング

ビジネスルール違反は `ExceptionOrNone.FromException` で表現し、`ResultBox` が自動的に例外を伝搬します。
API 層では `BadRequest` 等に変換できます。

## 複数値の伝播

`TwoValues`, `ThreeValues` などのヘルパーで複数の値を持ち回りできます。複数タグを扱う際に便利です。

## 非同期合成

`Combine` が `Task<ResultBox<T>>` を受け取るため、タグ状態の読み取りをそのままパイプラインに組み込めます。
順序制御も自動で行われます。

## 複数イベントの返却

ハンドラー内で `context.AppendEvent` を呼ぶと追加イベントを収集できます。`Conveyor` では最終イベントのみ返し、
エグゼキューターが重複を除去して永続化します。

## テスト

ハンドラーは `Task<ResultBox<EventOrNone>>` を返す静的メソッドなので、コンテキストをモックすれば単体テストが簡単
です。`IsSuccess` と `GetException()` を確認しましょう。
