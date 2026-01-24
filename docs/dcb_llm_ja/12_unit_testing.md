# テスト - DCB ドメインの検証

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
> - [テスト](12_unit_testing.md) (現在位置)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)

DCB にはインメモリ実装が用意されており、Orleans や実データベースなしでコマンド/プロジェクションの挙動を
テストできます。

## インメモリハーネス

- `InMemoryEventStore`
- `InMemoryObjectAccessor`
- `GeneralSekibanExecutor`

```csharp
var eventStore = new InMemoryEventStore();
var domainTypes = DomainType.GetDomainTypes();
var accessor = new InMemoryObjectAccessor(eventStore, domainTypes);
var executor = new GeneralSekibanExecutor(eventStore, accessor, domainTypes);
```

## 楽観的同時実行のテスト

`tests/Sekiban.Dcb.Tests/OptimisticLockingTest.cs` では、正しいバージョンと誤ったバージョンで予約が成功/失敗する様子を
検証しています。`ConsistencyTag.FromTagWithSortableUniqueId` を使い、意図したバージョンでイベントを記録できます。

## プロジェクターのテスト

プロジェクターは純粋関数なので、イベントの組を渡して戻り値を比較するだけで検証可能です。必要なら
`GeneralTagStateActor` をインメモリで利用し、実際の再生処理を模倣できます。

## クエリのテスト

- インメモリエグゼキューターでイベントを流し込む
- マルチプロジェクションに向けたクエリ (`QueryAsync`) を実行
- 戻り値の `ListQueryResult` や単一結果をアサート

Orleans 全体を起動したテストは `tests/Sekiban.Dcb.Orleans.Tests` を参照してください。

## アサーションのコツ

- `ResultBox` の `IsSuccess`, `GetValue()`, `GetException()` を活用
- `SortableUniqueId` を比較してイベント順序を検証
- テストでも `DomainType.GetDomainTypes()` を使い本番と同じ登録状態を再現

## CI での運用

- ユニットテスト: インメモリのみで高速実行
- 統合テスト: Orleans + Postgres を docker-compose / Aspire で起動
- 再現性: GUID や日時は固定値を使いイベントログを安定化
