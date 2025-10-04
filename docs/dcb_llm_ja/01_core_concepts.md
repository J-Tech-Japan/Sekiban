# コアコンセプト - Dynamic Consistency Boundary (DCB)

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md) (現在位置)
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
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)

## DCBとは？

Dynamic Consistency Boundary (DCB) は Sekiban が採用する次世代のイベントソーシング実行基盤です。集約ごとの
イベントストリームを使う代わりに、すべてのビジネス操作をグローバルに順序付けられた単一のイベントログに
記録し、そのイベントに関係するタグを付けます。どの境界を強整合にするかはコマンドの実行時に決まり、
エグゼキューターがタグの予約(Reservation)を協調させることで競合を防ぎます。

主なアイデア:

- **1コマンド=1ビジネスファクト**: 送金なら「送金完了」イベント1つにすべてを含めます。
- **単一のグローバルイベントストリーム**: `SortableUniqueId` で時系列を保証し、どのバックエンドでも同じ順序で再生できます。
- **タグによる境界指定**: `ITag` が `IsConsistencyTag()` を通じて一貫性境界への参加可否を示します。
- **楽観的同時実行制御**: 予約時に最後の `SortableUniqueId` を渡し、変化があれば即座に衝突を検知します。

## なぜDCBなのか

従来の集約志向のイベントソーシングは整合性境界が設計時に固定され、複数エンティティを横断する処理はサガや
補償処理が必須でした。DCB はその制約を取り除きます。

- **境界を動的に選択**: コマンドごとに必要なタグだけを予約するため、複雑なワークフローでも素直に表現できます。
- **整合性の担保**: 1回の `IEventStore.WriteEventsAsync` でイベント本体とタグをまとめて永続化します。
- **アクターモデルとの親和性**: Orleans などのアクターフレームワークがタグ単位でそのままマッピングされます。
- **監査容易性**: 1イベント=1ビジネス事実なので履歴トレースが簡単です。

## 集約ベースとの比較

| 観点 | 集約ベース | Dynamic Consistency Boundary |
| --- | --- | --- |
| イベントストリーム | 集約ごと | グローバル1本 |
| 整合性境界 | 設計時に固定 | コマンド実行時にタグで決定 |
| 複数エンティティの整合性 | サガ等による結果整合性 | 予約されたタグ内で即時整合 |
| 競合検出 | 集約バージョン | タグごとの `SortableUniqueId` |
| イベント構造 | 複数イベントで表現 | 1イベントでビジネスファクト |

## コアコンポーネント

- **イベント (`IEventPayload`)**: 不変のビジネス事実。詳細は `tasks/dcb.design/records.md` を参照。
- **タグ (`ITag`)**: 影響を受けるエンティティを識別。`"[Group]:[Content]"` 形式でシリアライズされます。
  例: `internalUsages/Dcb.Domain/Student/StudentTag.cs`。
- **GeneralSekibanExecutor**: コマンド検証、タグ状態取得、予約、永続化、配信を統括します。
  (`src/Sekiban.Dcb/Actors/GeneralSekibanExecutor.cs`)
- **TagStateActor / TagConsistentActor**: タグ状態のキャッシュと予約管理を担当するアクター。
  (`src/Sekiban.Dcb/Actors/GeneralTagStateActor.cs`, `GeneralTagConsistentActor.cs`)
- **イベントストア**: 順序保証とタグ検索を提供します。Postgres 実装は
  `src/Sekiban.Dcb.Postgres/PostgresEventStore.cs`、Cosmos 実装は
  `src/Sekiban.Dcb.CosmosDb/CosmosDbEventStore.cs`。

## メリット

1. **柔軟な整合性境界**: 必要なタグだけを予約するためボトルネックを最小化できます。
2. **クロスエンティティ処理の簡略化**: 調整用のサガを排し、ビジネスロジックを直線的に書けます。
3. **アクターによるスケーラビリティ**: タグ単位でアクターが分散配置され、ホットスポットの分離が容易です。
4. **リッチな読み取り**: マルチプロジェクションとタグ状態プロジェクターが高速な読み取りを提供します。
5. **可観測性**: `EventMetadata` に因果ID・相関IDが入り、追跡が容易です。

## 設計原則

- **整合性は必要なタグだけ**: `IsConsistencyTag()` が false のタグは投影だけに参加し、予約しません。
- **再実行前提**: コマンドは冪等であることを前提とし、失敗時はクライアント側でリトライします。
- **決定的なプロジェクション**: プロジェクターは副作用なしの静的メソッドで実装します。
- **ストレージ非依存**: `IEventStore` 契約を満たす任意のバックエンドで利用できます。

## 参考資料

- コンセプト詳細: `tasks/dcb.design/dcb.concept.md`
- インターフェース一覧: `tasks/dcb.design/interfaces.md`
- レコード定義: `tasks/dcb.design/records.md`
- DCB 概念サイト: <https://dcb.events>
