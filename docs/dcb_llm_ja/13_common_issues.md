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
> - [ストレージプロバイダー](11_storage_providers.md)
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

## Cosmos でイベントがタグスコープ読み取りに現れない

**症状**: `ReadAllEventsAsync` ではイベントが返るのに、`ReadEventsByTagAsync` では返らない。`TagExistsAsync` は false、タグプロジェクターも認識せず、(`GeneralTagConsistentActor` の楽観的並行性制御のベースラインにも使われる)`GetLatestTagAsync` が、書き込んだばかりのイベントより古い値を返す。

これは [issue #1046](https://github.com/J-Tech-Japan/Sekiban/issues/1046) で報告された症状です。この現象は Cosmos だけで起こります。Postgres はイベント行とタグ行を単一トランザクションで書くため、原理的に発生しません。

### まず: 遅延なのか、残骸なのか

最初の数秒間は見分けがつきませんが、必要な対応はまったく異なります。

- **一時的な遅延** — 書き込みがまだ進行中であるか、アカウントの整合性レベルによって読み手が古いレプリカを見ている。**待って読み直してください。** 自然に追いつくなら、何も問題はありません。
- **永続的な残骸** — タグ行がそもそも書かれていない。Cosmos の書き込みは2段階(イベント → タグ行)なので、その間にクラッシュするとイベントだけが永続化され、タグ行が存在しない状態になります。**いくら待っても直りません。**

イベントが書かれてから数分経ってもタグ読み取りに現れないなら、それは残骸です。

### 次に: テレメトリを確認する

`Sekiban.Dcb.CosmosDb` メーターは、書き込みが実際に失敗したかどうかを教えてくれます。

- `sekiban.dcb.cosmos.tag_write.failures` / `.retry_outcomes{outcome=exhausted}` — タグ書き込みが諦めた。構造化ログと `CosmosTagWriteExhaustedException` が該当イベントIDを通知します。
- `sekiban.dcb.cosmos.event_write.partial_failures` — 複数イベント書き込みで一部だけが着地した。

ただし **クラッシュはプロセス内に痕跡を残しません**。したがって **メトリクスが空でも、tags コンテナーが健全だとは限りません**。dry run で確認してください。

### 対処: タグインデックスを修復する

`tags` コンテナーは **派生可能なインデックス** です(すべてのイベントドキュメントが完全な `tags` 配列を保持しています)。したがって欠けた行はイベントから再構築できます。修復サービスを登録し、**必ず dry run から始めてください**。

```csharp
services.AddSekibanDcbCosmosDb(configuration);
services.AddSekibanDcbCosmosDbTagRepair();   // opt-in。AddSekibanDcbCosmosDb だけでは登録されない

var repair = await factory.CreateAsync(serviceId);

// 書き込む前に必ず確認する。DryRun が既定値。
var report = await repair.RepairAsync(new CosmosTagRepairOptions
{
    DryRun = true,
    ToSortableUniqueIdInclusive = lastSettledSortableUniqueId,  // 上限を固定する
});
```

**`ToSortableUniqueIdInclusive` には、確定済みと分かっているイベントを指定してください。** 指定しないとスキャンが末尾まで走り、稼働中のトラフィックと競合します。それらは書き込みパス自身が処理するものです。修復はクラッシュの残骸のためのものであり、いま書き込まれつつあるイベントのためのものではありません。

レポートを読んだ上で、`DryRun = false` で修復します。分類が、いま何を見ているのかを教えてくれます。

| 分類 | 意味 | 修復は書き込む? |
|---|---|---|
| `Missing` | この (イベント, タグ) を索引する行が存在しない。 | **はい — 書き込むのはこれだけ。** |
| `Present` | 導出された行が存在し、イベントと一致している。 | いいえ |
| `LegacyPresent` | 決定論的ID方式より前の行が索引している。**正常に機能します**。移行は任意。 | いいえ |
| `Duplicate` | 複数のレガシー行が索引している。 | いいえ — 報告のみ |
| `Corrupt` | 行は存在するがイベントと矛盾している。 | **いいえ — 決して上書きしません。** 何かする前に調査してください。 |
| `Overflow` | 1キーあたりの上限を超える行数。 | いいえ — `MaxRowsPerKey` を上げて再確認 |

`Corrupt` や `Duplicate` は、修復サービスが勝手に解決するものではありません。これは意図的な設計です。

### 防げる再発は防ぐ

`WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward` を設定してください。

```csharp
services.AddSekibanDcbCosmosDb(
    configuration,
    options => options.WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward);
```

既定値は `Compatible` で、タグ書き込みをリトライせず、(現在は非推奨の)`TryRollbackOnFailure` を通じて **書き込み済みのイベントを削除します**。マルチプロジェクションが既に読み取っているかもしれないイベントをです。`RollForward` は代わりにタグ書き込みをリトライし、イベントを削除することは決してありません。

**重要な注意点**: `RollForward` が効くのは、プロセスが生き残ってリトライできる場合だけです。**クラッシュはプロセス内の失敗ではありません**。したがって、どのポリシーでも防げない残骸が残ります。この窓を閉じられるのは修復パスだけです。[opt-in のスイープ](11_storage_providers.md)は直近のウィンドウに対して修復を自動実行できますが、あくまで *最終的な* 修復です。タグ読み取りをガードせず、スイープが到達するまで窓は開いたままです。

この窓を一切許容できないワークロード(とりわけ金銭に関わるもの)では、**Postgres プロバイダーを使用してください**。ここで述べたギャップはいずれも存在しません。

**参照**: [整合性契約](11_storage_providers.md#整合性契約) — プロバイダーごとの完全な保証、修復サービス、スイープ、そしてそれらが **保証しないこと**。

## WithoutResult: 失敗が素の `NullReferenceException` として届く / そもそも届かない

**症状**: `Sekiban.Dcb.WithoutResult` の API(`ISekibanExecutor`、`ICommandContext`、Orleans エグゼキューター)を呼ぶと、メッセージも呼び出し元の情報もない `NullReferenceException` が飛ぶ。あるいはさらに悪いケースとして、**例外がまったく飛ばず、明らかに誤った答えが返る**(存在するタグに対して `TagExistsAsync` が `false`、値型のクエリが `0`)。

**原因**: WithoutResult パッケージは `ResultBox` ネイティブなコアに対するファサードです。内部ではすべての操作が `ResultBox<T>` を返し、境界で箱を開けます。この「開ける」処理は従来 `UnwrapBox()` であり、その挙動は `T` の種類に依存していました。

| 箱の状態 | 呼び出し側が受け取っていたもの |
|---|---|
| 失敗、`T` が参照型 (例: `TagState`) | 保持していた例外を再スロー — 正しい |
| 失敗、`T` が**値型** (例: `bool`、`int`) | `default` — **失敗が黙って握りつぶされる** |
| 箱そのものが `null`(内部パスが `null` を返した) | メッセージも操作名もない `NullReferenceException` |

問題が深刻なのは 2 行目です。メッセージが不親切なのではなく、**答えが変わってしまいます**。`ICommandContext.TagExistsAsync` は `bool` を返すため、イベントストアに到達できなかった場合でも `false` が返り、「そのタグは存在しない」と区別がつきません。これをガード条件にしているコマンドハンドラーは、実際には存在するエンティティを新規作成してしまいます。

**10.3.0 で修正されました**。WithoutResult のすべての境界は、単一のポリシーで箱を開けます。

- **箱が保持している失敗は、そのまま再スローされます** — 同一の例外インスタンス、同一の型、同一のスタック。`T` が参照型でも値型でも同じです。既存の `catch (SekibanValidationException)` や `catch (OperationCanceledException)` はこれまでどおり動作し、キャンセルは元の `CancellationToken` を保持したままです。**変わったのは、値型の境界が握りつぶさずにスローするようになった点だけです。**
- **境界の情報は例外をラップせずに例外へ記録します**: `ex.Data["Sekiban.Boundary.Operation"]`(例: `ICommandContext.TagExistsAsync`)と `ex.Data["Sekiban.Boundary.Target"]`(例: タグ、コマンド型)。例外の型は変わりません。
- **再スローすべき失敗が存在しない場合**は、素の `NullReferenceException` ではなく、操作名を含む `SekibanBoundaryException`(名前空間 `Sekiban.Dcb.Boundaries`)が飛びます。これは内部パスが返してはならないものを返したことを意味します。メッセージを添えて Issue を立ててください。

**`SekibanBoundaryException` を受け取ったら**: これはドメインエラーではなく、リトライする対象もありません。Sekiban 内部が結果を返さなかったことを示します。メッセージ(`Operation` / `Target` プロパティを含む)を添えて報告してください。

## JSON シリアライズ例外

**対処**: イベントペイロードの変更は後方互換にする。`EventMetadata.EventType` をログに出し問題のイベントを特定。

## Azure Queue ストリームの欠損

**症状**: 投影が追いつかない。

**対処**: キューの存在・権限を確認。`BatchContainerBatchSize` や `GetQueueMsgsTimerPeriod` を調整。ローカルでは Azurite 設定を確認。

## Dapr 連携

DCB の Dapr 版は未提供です。`Sekiban.Pure.Dapr` は古いランタイム向けであり、DCB では Orleans をご利用ください。
