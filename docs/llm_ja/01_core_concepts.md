# コアコンセプト - Sekiban イベントソーシング

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md) (現在のページ)
> - [はじめに](02_getting_started.md)
> - [集約、プロジェクター、コマンド、イベント](03_aggregate_command_events.md)
> - [複数集約プロジェクター](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [ワークフロー](06_workflow.md)
> - [JSONとOrleansのシリアライゼーション](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントAPI (Blazor)](09_client_api_blazor.md)
> - [Orleansセットアップ](10_orleans_setup.md)
> - [ユニットテスト](11_unit_testing.md)
> - [一般的な問題と解決策](12_common_issues.md)
> - [ResultBox](13_result_box.md)

## コアコンセプト

イベントソーシング：すべての状態変更を不変のイベントとして保存します。現在の状態はイベントを再生することで導き出されます。

## 命名規則

- コマンド：命令形の動詞（Create、Update、Delete）
- イベント：過去形の動詞（Created、Updated、Deleted）
- 集約：ドメインエンティティを表す名詞
- プロジェクター：投影する集約にちなんで命名

## イベントソーシングの主要な原則

イベントソーシングは次のようなアーキテクチャパターンです：

1. **イベントとしての状態変更**：アプリケーションの状態に対するすべての変更はイベントのシーケンスとして保存されます
2. **不変のイベントログ**：記録されたイベントは変更または削除されることはありません
3. **プロジェクションによる現在の状態**：現在の状態はイベントを順番に再生することで計算されます
4. **完全な監査証跡**：イベントログはすべての変更の完全な履歴を提供します

## Sekibanを使用する利点

1. **完全な履歴**：ドメインの変更についての完全な監査証跡
2. **タイムトラベル**：任意の時点での状態を再構築する機能
3. **ドメイン重視**：明確なドメインモデルによる関心の分離の向上
4. **スケーラビリティ**：読み取りと書き込み操作を個別にスケールできる
5. **イベント駆動アーキテクチャ**：イベント駆動システムとの自然な統合

## コアコンポーネント

- **集約（Aggregate）**：状態とビジネスルールをカプセル化するドメインエンティティ
- **コマンド（Command）**：システム状態を変更するためのユーザーの意図を表す
- **イベント（Event）**：発生した状態変更の不変の記録
- **プロジェクター（Projector）**：イベントを集約に適用して現在の状態を構築する
- **クエリ（Query）**：現在の状態に基づいてシステムからデータを取得する

## PartitionKeys：イベントストリーム管理

PartitionKeysはSekibanの基本的な概念で、物理的なイベントストリームを管理します。各イベントストリームは次の3つの要素を持つPartitionKeysオブジェクトによって一意に識別されます：

```csharp
using Sekiban.Pure.Documents;

public record PartitionKeys(
    Guid AggregateId,
    string Group,
    string RootPartitionKey);
```

1. **AggregateId (Guid)**：特定の集約インスタンスの一意の識別子。これは通常、システムによって生成されるか、既存の集約のアドレス指定時に提供されるバージョン7のUUIDです。

2. **AggregateGroup (string)**：通常はプロジェクター名と同じです。同じグループを持つ集約はAggregateListProjectorを使用して簡単にクエリできます。これにより、関連する集約の論理的なグループ化が可能になります。

3. **RootPartitionKey (string)**：テナント分離とデータ分割に使用されます。デフォルトは"default"ですが、異なるテナント、環境、または他の論理的な分割間でデータを分離するために設定できます。

**PartitionKeysの使用例：**

```csharp
using Sekiban.Pure.Documents;
using Sekiban.Pure.Projectors;

// 新しい集約の場合（新しいAggregateIdを生成）
PartitionKeys keys = PartitionKeys.Generate<UserProjector>();

// 既存の集約の場合
PartitionKeys keys = PartitionKeys.Existing<UserProjector>(existingId);

// カスタムテナント/パーティションを使用
PartitionKeys keys = PartitionKeys.Generate<UserProjector>("tenant123");
PartitionKeys keys = PartitionKeys.Existing<UserProjector>(existingId, "tenant123");
```

**PartitionKeysの利点：**

1. **物理的なストリーム管理**：イベントの保存と取得方法を制御します
2. **グループ化**：AggregateGroupにより関連する集約を簡単にクエリできます
3. **マルチテナンシー**：RootPartitionKeyはマルチテナントアプリケーションのデータ分離を容易にします
4. **スケーラビリティ**：データの効率的なシャーディングとパーティショニングが可能になります

## イベントソーシングと従来のCRUDの比較

| 側面             | イベントソーシング                                  | 従来のCRUD                           |
|-------------------|------------------------------------------------|-------------------------------------------|
| データストレージ      | 不変のイベントログ                            | 変更可能な状態レコード                      |
| 状態管理  | イベントシーケンスから導出                    | 現在の状態の直接操作       |
| 履歴           | 完全な履歴が保持される                      | 限られた履歴または別のログが必要  |
| 並行処理       | イベントシーケンスによる自然な競合解決 | ロックまたは楽観的並行性制御が必要 |
| 監査証跡       | 組み込み                                       | 追加実装が必要         |
| 時間的クエリ  | 過去の状態に対するネイティブサポート            | 困難、追加設計が必要      |
| ドメインモデリング   | 振る舞いが豊富なドメインモデルを奨励         | 貧血ドメインモデルに陥りやすい        |

## Sekibanアーキテクチャ

Sekibanは次のようなクリーンでモダンなイベントソーシングアプローチを実装しています：

1. **Orleansとの統合**：高度にスケーラブルな分散ランタイム
2. **JSONシリアライゼーション**：柔軟で人間が読めるイベントストレージ
3. **強力な型付け**：型安全なコマンド、イベント、集約
4. **最小限のインフラストラクチャ**：最小限の設定でのシンプルなセットアップ
5. **ソース生成**：ドメインタイプの自動登録