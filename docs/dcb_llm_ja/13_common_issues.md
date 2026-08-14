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

**経緯**: [issue #1045](https://github.com/J-Tech-Japan/Sekiban/issues/1045) は `CosmosDbEventStore.WriteEventsAsync` からの `NullReferenceException` として報告されましたが、原因は **2 つあり、Sekiban 側の欠陥はそのうち片方だけ**でした。

- **報告者側の原因 — stale な状態であり、Sekiban の欠陥ではない**: 当該の staging 環境はイベント/プロジェクターのバージョニング規律なしに頻繁に再デプロイされており、古いプロジェクション状態が蓄積した結果、読み取りが null を返すようになっていました。対象ストアを作り直すと解消し、バージョニングを正しく運用している本番環境では発生していません。同様の症状が出たら、まずプロジェクターのバージョニングを確認してください。
- **Sekiban 側の原因 — 診断上の欠陥**: その null が `UnwrapBox()` を通じてメッセージのない素の `NullReferenceException` として表面化したため、環境の問題がライブラリのバグにしか見えず、原因特定に不必要な時間がかかりました。**修正したのはこちら側の半分です。**

**さらに、その修正作業中に見つかった第 3 の問題**(誰も報告していなかったもの): 同じ `UnwrapBox()` が、**値型の境界で失敗を黙って握りつぶしていました**。これは報告者の stale 状態とは無関係な、Sekiban 自身の正しさの欠陥です。以下で説明します。

**原因**: WithoutResult パッケージは `ResultBox` ネイティブなコアに対するファサードです。内部ではすべての操作が `ResultBox<T>` を返し、境界で箱を開けます。この「開ける」処理は従来 `UnwrapBox()` であり、その挙動は `T` の種類に依存していました。

| 箱の状態 | 呼び出し側が受け取っていたもの |
|---|---|
| 失敗、`T` が参照型 (例: `TagState`) | 保持していた例外を再スロー — 正しい |
| 失敗、`T` が**値型** (例: `bool`、`int`) | `default` — **失敗が黙って握りつぶされる** |
| 箱そのものが `null`(内部パスが `null` を返した) | メッセージも操作名もない `NullReferenceException` |

issue #1045 が踏んだのは 3 行目です。そして 2 行目は誰もまだ踏んでいませんが、より深刻です。メッセージが不親切なのではなく、**答えが変わってしまいます**。`ICommandContext.TagExistsAsync` は `bool` を返すため、イベントストアに到達できなかった場合でも `false` が返り、「そのタグは存在しない」と区別がつきません。これをガード条件にしているコマンドハンドラーは、実際には存在するエンティティを新規作成してしまいます。

**10.3.0 で修正されました**。WithoutResult のすべての境界は、単一のポリシーで箱を開けます。

- **箱が保持している失敗は、そのまま再スローされます** — 同一の例外インスタンス、同一の型、同一のスタック。`T` が参照型でも値型でも同じです。既存の `catch (SekibanValidationException)` や `catch (OperationCanceledException)` はこれまでどおり動作し、キャンセルは元の `CancellationToken` を保持したままです。**変わったのは、値型の境界が握りつぶさずにスローするようになった点だけです。**
- **境界の情報は例外をラップせずに例外へ記録します**: `ex.Data["Sekiban.Boundary.Operation"]`(例: `ICommandContext.TagExistsAsync`)と `ex.Data["Sekiban.Boundary.Target"]`(例: タグ、コマンド型)。例外の型は変わりません。
- **再スローすべき失敗が存在しない場合**は、素の `NullReferenceException` ではなく、操作名を含む `SekibanBoundaryException`(名前空間 `Sekiban.Dcb.Boundaries`)が飛びます。これは内部パスが返してはならないものを返したことを意味します。メッセージを添えて Issue を立ててください。

**`SekibanBoundaryException` を受け取ったら**: これはドメインエラーではなく、リトライする対象もありません。Sekiban 内部が結果を返さなかったことを示します。メッセージ(`Operation` / `Target` プロパティを含む)を添えて報告してください。

## ホストが起動を拒否する: 「Production requires a distributed runtime」

**症状**: `AddSekibanDcbProductionGuard()` を有効にしたら、起動時に `SekibanDcbProductionGuardException` が発生する。

**これはガードが正しく機能している状態です。** ガードはエグゼキューターを解決して「何者か」を問い合わせ、その答えが分散ランタイムではありませんでした。つまり、インプロセス(テスト用)エグゼキューター、あるいは自分が何かを申告しないエグゼキューターが、ガードから見て Production の環境に登録されています。インプロセスのアクターにはクラスタ協調がなく、**複数ホストは互いのタグ予約を見られません**。DCB が守るべき不変条件が守られないということです。**これを許可するオーバーライドはありません。**

**対処**: 分散ランタイムのエグゼキューターを使ってください。もしこれが「Production という名前が付いただけ」のローカル環境なら、名前を変えるか、単一サイロの localhost Orleans ホストを使ってください(本物の分散ランタイムであり、インストール不要です)。[localhost Orleans](22_localhost_orleans.md) を参照。

## 起動バナーがストレージを Volatile と表示する

テスト以外の環境では、それはデータ損失です。[ストレージプロバイダー](11_storage_providers.md#耐久性ディスクリプタと本番ガード)を参照してください。

## 行に値があるのにイベントの値が null で返る (#1074)

**症状**: クエリが空を返す、または投影が populated に見えない。保存済みイベントを確認すると JSON には明らかに値が入っているのに、デシリアライズされたペイロードのプロパティが全て null / 0 / 既定値。読み取り時に例外は発生していない。

**原因**: 保存済みペイロードのプロパティ名が、読み取り側がバインドする際のケーシングと一致していません。Sekiban は camelCase で、大文字小文字を区別して読み書きします。PascalCase で書かれたペイロード(例: プロデューサーがドメインのオプションではなく素の `JsonSerializer.Serialize(x)` でシリアライズした場合)は、メンバー名(`StudentId`)が宣言済みメンバー(`studentId`)にバインドしません。System.Text.Json は大文字小文字を区別してバインドするため、一致しなかったメンバーを既定値のまま残し、成功として報告します。プロデューサー側のデータバグが、読み取り側では全 null のインスタンスになり、原因を指すものが何もありません。

**10.4.0 で修正されました**(SEK-G13): 既定でこの形に対して**明確に失敗する**ようになりました。トップレベルのメンバーがバインドせず、かつケーシングを除いて宣言済み名と一致する場合、`SekibanEventPayloadBindingException` を投げます。イベント型・CLR 型・問題の JSON 名・期待される名前・ペイロード位置を示し、**ペイロードの値は決して含めません**。本当に未知のメンバー(新しいライターの追加フィールド)は引き続き無視されるので前方互換性は保たれ、正しい camelCase の行には一切影響しません。チェックは契約上トップレベルのみで、ネストしたオブジェクトには再帰しません。

**対処**:

1. **プロデューサーを直す。** 素の `JsonSerializer.Serialize` ではなく、ドメインのオプション(camelCase)を通してシリアライズしてください。これが本当の修正です。例外は実在のデータバグを指しています。
2. **移行中に既存の誤ケーシング行を読む**には、ドメイン型を構築する際にデシリアライズポリシーを選びます:

   ```csharp
   var domainTypes = DcbDomainTypesExtensions.Simple(
       configure,
       deserializationPolicy: EventPayloadDeserializationPolicy.CaseInsensitiveLegacy);
   ```

   `CaseInsensitiveLegacy` はトップレベルのメンバーを、ケーシングに関わらず宣言済みの対応先へバインドします。これは移行の補助であって修正ではありません。保存済みデータを書き換えず、ネストしたケーシングには何もしません。

### 4 つのポリシー

| ポリシー | トップレベルのケース不一致 | 未知フィールド | 用途 |
|---|---|---|---|
| `FailOnCaseMismatch`(既定) | 例外 | 無視 | 新規システム。#1074 を捕捉 |
| `CompatibleCaseSensitive` | null にバインド(G13 以前) | 無視 | 旧来の沈黙から移行する間の一時的な逃げ道 |
| `StrictUnmapped` | 例外 | 例外 | 完全一致でないペイロードを一切受け付けない場合 |
| `CaseInsensitiveLegacy` | バインド(トップレベル) | 無視 | 移行中にレガシーの誤ケーシング行を読む |

識別子など必ず存在すべきメンバーは、C# の `required` または `[JsonRequired]` で宣言してください。必須メンバーの欠落は同じ記述的な例外で失敗します。これが存在を強制する唯一の安全な方法です(一律の null チェックは、正当な既定値まで拒否してしまいます)。

## クエリが空を返すが、イベントは存在し durable である (#1075)

**症状**: リストクエリが 0 件を返す、または投影が未 populated に見えるのに、イベントはストアに明らかに存在する。例外もエラーもなく、ただ「データがない」。#1074 と併発しがち — fold できないペイロードがまさにこの沈黙を生みます。

**原因**: 投影がイベントの 1 つを適用できず(fold が throw した、あるいはペイロードがデシリアライズできなかった)、その失敗が握りつぶされていました。投影は poison イベントで前進を止め、それまでに構築できたもの(多くの場合は空)を**成功した**空の結果として提示しました。「durable なイベントは存在するが投影できない」が「データがない」に化け、これは気づくのが最も高くつく種類の失敗です。

**10.4.0 で修正されました**(SEK-G14)。イベントを fold できない投影は **fault** し、fault は空を返す代わりにコンテキスト付きでクエリを失敗させます。

- インメモリのリプレイはもう握りつぶしません。読み取り失敗も fold 失敗も、エグゼキューター通常の `ResultBox`/例外境界からエラーとして表面化します。
- Orleans のマルチプロジェクションは**確定 fault**(イベント id・型・プロジェクター・位置)を記録し、未解決の間はすべてのクエリ面(state・スカラー・list)がそのコンテキストで失敗します。fault は永続化されるため、再アクティベートされた grain は最初のクエリに答える前に fault を再確立し、一瞬でも空を報告しません。
- **通常のキャッチアップ遅延は fault ではありません。** 単に遅れているだけの投影はクエリに答えます。イベントで**クラッシュした**投影だけが失敗させます。

### fault と lag の契約

| 状況 | クエリの挙動 |
|---|---|
| 健全・追いついている | 成功 |
| 健全・まだキャッチアップ中(遅延) | 成功(部分/現在の状態) |
| **fault**(イベントを fold できなかった) | **fault コンテキスト付きで失敗** — イベント id・型・プロジェクター・位置。ペイロードの値は含めない |

**投影 fault でクエリが失敗したときの対処**:

1. メッセージがイベント・プロジェクター・位置を示します。そのイベントを特定してください。
2. ケーシング/デシリアライズの問題なら #1074 です。プロデューサーを直すか、移行中は `CaseInsensitiveLegacy` デシリアライズポリシーで読んでください。
3. プロジェクターの fold ロジックが原因なら、プロジェクターを直してください。
4. 原因を解消したら、下記の運用者リセットで**投影を再構築**してください。fault は、同じ位置を正常にリプレイする決定的な再構築でのみクリアされます。無関係な後続イベントではクリアされません。poison がまだ残っていれば、再構築は同じイベントで再び fault します。

poison イベントのスキップ/隔離は**意図的に既定ではありません**。適用できないイベントを投影が黙ってスキップするのは、同じ種類の沈黙への逆戻りです。将来、明示的な opt-in ポリシーとして提供される可能性があります。

### 運用者リセット: `ResetProjectionFaultAsync`(管理面・運用者専用)

永続化された投影 fault のクリアは、グレインの管理インターフェース(`IMultiProjectionGrain`、`GetStatusAsync` と同じ面)における**運用者専用**の操作です。**自動的には決して呼ばれず**、`ISekibanExecutor` にも公開されません。アプリケーション/クエリのコードからは起動できません。

- **まずトークンを取得**: リセットには、現在の fault の*正確な*識別情報 — プロジェクター名・fault イベント id・fault ストリーム位置 — を並行性トークンとして渡す必要があります。失敗したクエリで表面化する fault コンテキスト(投影 fault エラーがイベント id・型・プロジェクター・位置を保持)から取得してください。合成してはいけません。
- **永続 descriptor が権威**: トークンは単一ライター gate 内で現在の*永続* fault と照合されます。古いトークン・並行書き込みで変わった descriptor・プロジェクター不一致・fault 不在は通常のエラーで拒否され、**書き込みも fault クリアも行われません**。同一トークンのレースは高々1回だけ commit します。
- **派生状態を再構築**: 正しいトークンは descriptor **と**派生チェックポイントを耐久的にクリアし、投影は catch-up で**先頭から再構築**されます。権威イベントは一切削除されません(グレインの派生スナップショット/チェックポイントのみ)。first-query barrier により、再構築が head に到達する前に「健全」応答が漏れることはありません。
- **クリアは獲得するもの**: 耐久的クリアが commit された後にのみライブアクター fault がクリアされ、新規アクティベーションが要求されます。poison が依然として fold できなければ、再構築時に per-event 境界が fault を**再確立・再永続化**します — リセットはスキップや隔離をしません。恒久的なクリアは同じ位置が正常にリプレイされたときにのみ起こります。
- **部分失敗の意味論(2つのストア)**: リセットは2つの派生ストア — グレイン状態(descriptor)と外部スナップショットストア — を、単一ライター gate 内で「トークン検証 → 外部スナップショット無効化 → descriptor の耐久的クリア」の順に操作します。単一の原子的トランザクションでは**ありません**:
  - **外部無効化が失敗**した場合、descriptor クリアはスキップされ、descriptor・ライブ fault・外部スナップショットはすべて保持されます(何も変わらない)。ストア回復後に同じトークンで再試行してください。
  - 外部無効化が**成功したが descriptor クリアが失敗**した場合、外部スナップショットは既に消えていますが descriptor とライブ fault は保持され、全クエリは拒否のまま(fail-closed)です。これは整合的です: 後続の rebuild がスナップショットを再生成し、保持された descriptor が再試行まで projection を拒否状態に保ちます。削除されて未再生成のスナップショットは無害です(派生専用であり、権威イベントは決して削除されません)。
  - **進行中の upsert が delete と競合することはありません**: **すべての**外部スナップショット変更が1つのアクティベーションローカル coordinator を通ります — 3つの upsert パス（通常 persist・streaming persist・version rewrite）**とすべての delete**、すなわち公開 admin API の `DeleteExternalStateAsync` とリセット自身の invalidation を含みます。ストアへの直接バイパスは残っていません。そのため delete は park 中/進行中の upsert 完了を待ってから削除し、delete と並行して upsert が走ることはありません。さらに coordinator は **live または committed の fault 存在時に upsert を拒否**するため、faulted な projection は派生状態を永続化せず、stale upsert がリセットの削除対象を再生成することもありません。catch-up は interleaving のグレインタイマー上で走るため、この順序を保証するのは非リエントランシではなくこの coordinator です。(耐久的な epoch/tombstone は、現行モデルにはない multi-writer や cross-silo の永続化でのみ必要になります。)
  - fault による拒否は **暗黙の成功ではなく明示的な失敗**です: ブロックされた upsert は、成功値 `false` ではなく、安定した `ExternalPersistenceBlockedByFaultException` を保持する `ResultBox.Error` を返します。これは重要で、`IsSuccess` だけを見る呼び出し側が拒否を「保存完了」と誤認しないためです — 通常 persist と streaming persist は拒否後に「保存済み」を報告せず永続化メタデータも進めず、version rewrite は `updated = false` を維持し projector-version の書き込みを一切行いません。

## 条件付き（ユニークキー）追記: in-doubt・キー再利用・Cosmos タグ修復ゲート

これは任意の SEK-G15/G16 `IConditionalEventStore.AppendIfUniqueAsync` コントラクト（[ストレージプロバイダー](11_storage_providers.md) 参照）に関するものです。無条件書き込みパスには影響しません。

**症状と各アウトカムの意味**:
- `ConditionalAppendInDoubtException` を伴う `ResultBox.Error`（WithoutResult では同じ例外が送出）。追記を確定した結果に解決**できなかった**状態。これは**リトライ可能**（`IsRetryable == true`）であり、第5の成功／衝突ステータスではありません。
- `KeyReuseConflictException`。冪等キーが既に**異なる**操作でクレームされています（永続化されたフィンガープリントが異なる）。一時障害ではなくプログラミングエラーです。
- `DynamoConditionalAppendLimitException`。DynamoDB `TransactWriteItems` の制限超過（100 アイテム = 1 イベント + 100 タグ超、またはタグ文字列重複によるアイテムキー重複）。**あらゆるネットワーク呼び出しの前**に送出。恒久的でリトライ不可。
- `ConditionalAppendCommittedStateCorruptionException`。同一操作のリトライが、イベントと**不一致**の既存コミット済みインデックス／タグ行を検出。これは**リトライ不可**（`IsRetryable == false`）で、行は決して上書きされません。調査してください。ループでリトライしないこと。
- `ConditionNotSupportedException`。解決されたストアが要求された書き込み条件を強制できない。無条件書き込みへ降格せず、ストア呼び出し前にフェイルクローズ。

**原因と対処**:
- **読み戻せる勝者のない衝突後、またはキャンセル／タイムアウト後の in-doubt**（`ReasonCode` は `winner-unreadable-after-conflict` / `ambiguous-after-write`）: ストアがクレーム衝突を示した（または耐久コミットの可能性がある後にキャンセルされた）が、検証のためのコミット済み勝者を読み戻せなかった。**リトライ**してください — 勝者がコミットすればリトライは `AlreadyCommittedSameOperation` に収束します。耐久コミット後のキャンセル／タイムアウトは、まず権威的な読み取り＋フィンガープリント（Cosmos ではコミット済み状態ゲートも）で可能なら `AlreadyCommittedSameOperation` に解決し、それ以外の場合のみ in-doubt として表出します。
- **コミット済み状態を検証できなかった in-doubt**（`ReasonCode` は `committed-state-unverified`）: これは **Cosmos 固有**です。Cosmos はイベントドキュメントとタグ行を別フェーズで書くため、イベント作成後のクラッシュはタグスコープ可視性が未回復のコミット済みイベントを残します。同一操作のリトライは、`AlreadyCommittedSameOperation` を返す**前**に**欠損**タグ行を冪等に修復します。修復が一時的になお失敗すれば（タグストアがまだ障害中）結果は in-doubt であり、タグ行欠損中に偽の `AlreadyCommitted` を返すことはありません。**対処**: リトライ。継続する場合は Cosmos のタグ修復／スイープ（[ストレージプロバイダー](11_storage_providers.md) 参照）を実行してください。
- **リトライ不可なコミット済み状態の corruption**（`ConditionalAppendCommittedStateCorruptionException`）: 同一操作のリトライが、決定論的識別子の下に既存するがイベントと**不一致**のタグ行（厳密な内容不一致）を検出。欠損行と異なり、不一致行は整合性違反であり、上書きされず、リトライでは解消しません。**対処**: 競合行がその識別子を占有した経緯（想定外の外部ライター、失敗したマイグレーション）を調査してください。ループでリトライしないこと。
- **想定外のキー再利用衝突**: 同じ冪等キーで異なるペイロード／タグが送信されました。キーは呼び出し側ではなく*操作*を表す必要があります。操作の同一性から導いた安定キー（例: マイグレーション名）を使い、全ホストで同一のイベントを構築してください。
- **DynamoDB の制限エラー**: タグ数を ≤99（1 イベント + 99 タグ = 100 アイテム上限）に減らし、タグ文字列を重複排除してください。条件付き追記は契約上シングルイベントです。
- **シークレットセーフ**: これらの例外はいずれも生の冪等キーやペイロードを持ちません — 不透明なフィンガープリント、プロバイダ名、ServiceId、*導出* EventId（corruption では導出行IDハッシュも）のみです。リクエストからキーを復元するようなログを追加しないでください。

### 書き込み後の失敗タクソノミー（それぞれの対処）

失敗が耐久コミットに対していつ起きたかで対処が変わります。以下は別物であり、すべての書き込み後エラーをリトライ可能／成功として扱わないでください:

- **(a) コミット前ロールバック（既知のプレコミット失敗）** — コミット**前**に失敗（またはキャンセル）し、耐久クレームは存在しない。**元の**プロバイダ／キャンセル／トランスポート例外そのもの（元の `OperationCanceledException` と `CancellationToken` を保持）として表出し、型付き `AmbiguousAfterWrite` にはならない（それはプロバイダ宣言のコミット後曖昧専用）。**対処**: リトライ（キーをクレームして収束）。*証明*: SQLite / Postgres の「コミット前キャンセル」テスト（実DB）— 耐久物なし、元のキャンセルが表出、リトライで append。
- **(b) 耐久コミット後の応答喪失（トランスポートまたはキャンセル）** — トランザクション／イベント＋行が耐久コミットした後に応答喪失。プロバイダは内部のコミット後曖昧マーカーを送出し（生のトランスポート例外は決して表出しない）、共有オーケストレーターが**同一呼び出し内**で、**境界付き**・呼び出し側非依存の検証バジェット下で権威的に解決（勝者読み取り＋フィンガープリント、Cosmos ではコミット済み状態ゲート）。元の勝者レシートで `AlreadyCommittedSameOperation` を返す。**対処**: 多くは不要（同一呼び出しが既に勝者レシートを返す）。*証明*: Postgres（実DB）・SQLite（実DB）・Cosmos マルチタグ（インメモリダブル上の本番フォールトシーム）のコミット後テスト。いずれも同一呼び出しでの AlreadyCommitted と厳密な勝者レシート一致をアサート。
- **(c) 境界付き検証が完了できない** — 同一呼び出しの権威的検証が独立バジェットを超過（ハング／障害中プロバイダ）、または読める勝者がいない。元の原因とその正確な `CancellationToken` を保持した型付きリトライ可能 `ConditionalAppendInDoubtException`（`AmbiguousAfterWrite`）として表出。呼び出し側のキャンセルが検証をキャンセルすることはない。**対処**: リトライ（勝者が読める／コミットされれば収束）。*証明*: 共有オーケストレーターに対する境界付き検証ユニットテスト（バジェット内の読み戻し→AlreadyCommitted、バジェット超過のハング→即座に型付き `AmbiguousAfterWrite`）。
- **(d) 勝者を解決できない衝突** — クレーム衝突を解決できない（勝者を読めない、またはコミット済み状態を検証できない）。閉じた `Reason` を持つ型付きリトライ可能 `ConditionalAppendInDoubtException` として表出。**対処**: リトライ（勝者が読める／コミットされれば収束）。*証明*: Cosmos の裸409-勝者なし・修復失敗テスト、DynamoDB の裸衝突-勝者なしテスト。

プロバイダテストの忠実度: Postgres は実コンテナ（Testcontainers）、SQLite は実一時ファイルDB、Cosmos はインメモリ忠実ダブル上の本番フォールトシーム、DynamoDB はスレッドセーフ fake。「実プロバイダ」の主張は Postgres/SQLite のみに当てはまります。

## JSON シリアライズ例外

**対処**: イベントペイロードの変更は後方互換にする。`EventMetadata.EventType` をログに出し問題のイベントを特定。

## Azure Queue ストリームの欠損

**症状**: 投影が追いつかない。

**対処**: キューの存在・権限を確認。`BatchContainerBatchSize` や `GetQueueMsgsTimerPeriod` を調整。ローカルでは Azurite 設定を確認。

## Dapr 連携

DCB の Dapr 版は未提供です。`Sekiban.Pure.Dapr` は古いランタイム向けであり、DCB では Orleans をご利用ください。

## Serialized-Commit ワイヤ契約の互換性 (SEK-G17)

**症状**: バージョン混在デプロイで serialized-commit ワイヤ契約が「壊れている」と報告される、あるいは構造的におかしい
リクエストに対しエンドポイントが null 参照の 500 を返す。

**原因**:
- 公式契約 (`eventCandidates` + base64 `payload` + `eventPayloadName` + **イベント単位の `tags`** + `consistencyTags`) は
  dcb-v10.2.2 → 10.6.0 で変わっていません。#1087/#1088 は、報告された破損を、同じルートで公開され本契約を写したものと
  誤説明された別の下流形状 (`events` / `payloadJson` / per-commit-`tags`) に起因すると特定しました。
- フレームワーク契約には従来、バージョン識別子・固定プロパティ名・正規仕様・ゴールデンワイヤテストがなかったため、
  誤った互換性主張を機械的に失敗させる対象が存在しませんでした。

**対処**:
- `07_json_orleans_serialization.md` の正規仕様 (「Serialized Commit ワイヤ契約」) を正とし、互換性主張は必ずゴールデン
  ベクタ (`SerializedCommitWireGoldenTests`) を通過させます。
- 命名を「直す」ために positional DTO へシリアライズ属性を追加しないでください。代わりに契約所有の
  `SerializedCommitWireContract` / `SerializedCommitWireJsonContext` で固定します。属性は fresh-options 利用者の
  PascalCase 出力を変えてしまいます。
- 明示的なバージョンと型付きエラーが必要なら `ISerializedCommitAcceptor` / `SerializedCommitAcceptor` 経由で受理します。
  未知の `version` は `UnsupportedSerializedCommitEnvelopeVersionException`、不正形状は `MalformedSerializedCommitException`
  で、いずれも副作用の前に fail closed となり、null 参照の 500 になりません。
- イベント単位のタグをコミット単位へ縮約する下流アダプタは、コミット内の全イベントが同一タグ集合を共有する場合のみ
  許され、それ以外は明示的に拒否しなければなりません。

## マルチプロジェクションのインスタンス間乖離 / チェックポイントのずれ (SEK-G18)

**症状**: スケールアウト構成(共有イベントストア上の 2 台以上)で、同一クエリに対し 2 インスタンスが
競合 create エンティティに異なる値を返し続け収束しないのに `IsSafeState=true` を報告する。あるいは
新規イベントゼロの独立ホスト再起動をまたいで、永続チェックポイントの position/`EventsProcessed` が変化する。

**原因**:
- served(unsafe)状態がイベントを到着順に畳み込み、グローバル順の safe 状態と再構成されていなかった
  (#1092)。そのため最初に到着したものが恒久的に勝ち、`IsSafeState` はタイムスタンプ比較で付与された
  (未再構成ペイロードに対する虚偽)。
- チェックポイント復元時、キャッチアップ開始位置がレコードの `LastSortableUniqueId` からではなく
  再推論され、反映済みイベントを再度畳み込んだ(#1086)。
- 永続化される `EventsProcessed` が served の総数(safe + まだ unsafe)だったのに対し、レコードの位置は
  safe 位置。復元時にこの総数をカウンタの初期値とし、safe 位置からの排他的キャッチアップが
  まだ unsafe のイベントを再加算して**二重計上**していた。さらにクラスタ間の staleness ガードが
  safe 同士ではなく total 同士を比較していた(#1086 / #2)。

**対処**(フレームワーク SEK-G18 — アップグレード以外の対応は不要):
- served 状態は昇格のたびに `safe + 順序付き残バッファ` として再導出されます。`IsSafeState=true` は
  served が safe と同一であることを意味します。圧縮ベースライン上でグローバル順から外れた safe 昇格は
  権威ストアからの完全な順序付き再構築を発動し、クエリは再構築バリアを待つか fail-closed します
  (古い値で success は返しません)。
- 復元後のキャッチアップはレコードの `LastSortableUniqueId` を排他的に読み取って開始します。
- 永続化される `EventsProcessed` は safe チェックポイントのカウント(永続化した safe 位置で safe 状態に
  反映済みのイベント数)となり、その位置と対で保存されます。よって復元 + 排他的キャッチアップは
  まだ unsafe のイベントをちょうど一度だけ再加算し(二重計上なし)、クラスタ間の staleness ガードは
  safe 同士で比較します。旧来の総数で書かれたレガシーレコードは自身の safe カウント以上なので、比較は
  最悪でも保守的にスキップ寄りになるだけで、stale 上書きは起きません。(排他的な after-position 境界は
  全プロバイダ経路で**実行可能ベクタ**により固定——in-memory + SQLite はコアテスト、実 Postgres は CI の
  Testcontainers、Cosmos・DynamoDB・Hybrid は各実ストアの本番読み取り構築を忠実なシームで駆動し、
  `> since` が `>= since` に退行すれば失敗します。)
- アプリのプロジェクタは create アームを真の first-event-wins(`if (state.Contains(id)) return state;`)
  で実装し、グローバルに最も早いイベントが権威となるようにしてください。

### クラスタ間・共有ストア収束 — G20（10.8.0)で解消済み

**これは SEK-G18 の残余であり、SEK-G20 で解消されました。** 2つ以上の*独立した*クラスタが 1 つの外部
チェックポイント行(`dcb_multi_projection_states`、`serviceId/projectorName/projectorVersion` がキー)を
共有し、一方が retrograde な完全再構築を行ったとき、まだ古いチェックポイントを保持する別クラスタが、
retrograde イベントを観測する前にその行を——retrograde 後の**より後方(later)の safe 位置**で——再 upsert
(再汚染)し得ました。これは**恒久的な false-safe** でした:再汚染された later 位置を fresh な活性化が復元
すると、排他的 after-position キャッチアップがその位置の *後* から始まり、**欠落した earlier イベントを
恒久的にスキップ**し、権威イベントを欠いた状態に `IsSafeState=true` を報告し続け、自己修復しませんでした。

**G20 での解消方法** — 共有チェックポイント行への 2 層 CAS
([ストレージプロバイダー → generation-aware checkpoint CAS](11_storage_providers.md#sek-g20-generation-aware-checkpoint-cas) 参照):
- すべての product チェックポイント永続化は**期待トークン CAS**。stale writer(別クラスタが tombstone する
  前に park していた peer)は `ConditionRejected` となり、**行を再汚染しません**——これが中核の修正です。
- 無効化は delete ではなく耐久的な **generation bump + tombstone**。ローカル再構築の前に他クラスタへ可視。
  従前のペイロードは tombstone 下に保持されます。
- fresh な活性化はペイロード束縛の**前**に制御面(generation / 正確なトークン / lifecycle)を読み、tombstone
  なら完全な順序付き再生を強制し、正確な tombstone トークンで rebuilt-commit(同一行 1 CAS)します。勝者は
  ちょうど 1、敗者は refetch。
- 非対応ストア(capability 未実装の独自 `IMultiProjectionStateStore`)は retrograde 無効化時に黙って再構築せず、
  G14 fault パスへ**フェイルクローズ**します。

既存のチェックポイント行は generation 0 / revision 0 / Active として読まれます——プロバイダごとの加算スキーマ
アップグレード(実 pre-G20 DB で検証)で、イベント/ペイロードの移行はありません。**mixed-version の注意**:
SQLite ではレガシーの `INSERT OR REPLACE` upsert が制御列をリセットするため、pre-G20 の WRITER が tombstone を
消し得ます——全 writer が 10.8.0 にアップグレードされて初めて保護が完成します。完全な修正のリリースゲートは
G18 + G19 + G20 です。

## 単一クラスタでの重複初回書き込み — G19 (10.8.0) で解決

**症状**。同じ整合性タグへの2つの作成コマンドが単一クラスタで両方成功し（非オーバーラップな予約）、アプリは
「このIDは既に存在する」というコマンドエラーに依存できず、すべての作成プロジェクターが重複許容を強いられていました。

**原因**。タグの初回書き込み予約が、期待バージョンと現在バージョンの*両方*が非空の場合にのみ比較していました。空の
期待バージョン（初回書き込み）はチェックをスキップしたため、既にコミット済み状態を持つタグへの2つ目の初回書き込みが
通過していました。

**G19 による解決**。予約はアクターロック内・catch-up 後に行われ、空の期待バージョンは「タグが空であることを期待する」
を意味します。そのため、2つ目の非オーバーラップな初回書き込みは既存の `ResultBox.Error` チャネルで**衝突**します
（新しい公開例外型なし）。その後 SEK-G30 は、未観測タグ（`null`、Unspecified: 比較なしで予約）と、正常に空を観測した
タグ（`""`、AssertEmpty）を区別しました。非空バージョンは引き続き ExactMatch です。
[3状態の予約セマンティクス](03_aggregate_command_events.md#3状態の予約セマンティクス-sek-g19--sek-g30-10110)参照。

**境界（クラスタ単位）**。これは**クラスタあたり最大1回の初回書き込み**を保証します（タグごとに1つの Orleans アクター
活性化）。独立したクラスタはアクターを介して協調しません。クラスタ間の一意性はストレージ層の条件付きユニーク追加
（G15/G16）、永続化された重複の収束は SEK-G18 が担います。**動作変更**: 10.8.0 から競合する作成の一方が整合性エラーで
失敗します。完全な修正のリリースゲートは G18 + G19 + G20 です。

## 正しいクラスタ間更新が空のアクターキャッシュに対して拒否される — 10.8.2 で解決 (SEK-G22)

**症状**。コマンドのタグ状態 fold は別クラスタの永続書き込みを参照しているのに、予約が
non-empty-expected/empty-current 衝突で失敗します。再試行やアクター再活性化では同じコマンドが成功する場合があります。

**原因**。タグ整合性アクターはリモートコミット前にタグを空として正常にキャッシュしていました。その活性化には通知が
届かない一方、コマンドの fold は独立して共有ストアの新しい状態を読み取っていました。

**修正**。この stale-empty 形だけが、予約ロック内で `GetLatestTagAsync` による権威再確認を1回行います。一致なら成功、
空のまま・別バージョン・読み取り失敗なら fail-closed です。成功した結果は完全一致判定より先にすべてキャッシュへ採用
されるため、後続の expect-empty 予約が G19 を再び破ることはありません。通常パスと期待バージョンが空のパスには追加
読み取りがありません。

**境界**。これは誤拒否の解消であり、クラスタ間一意性の追加ではありません。クラスタ間一意性には G15/G16 の条件付き
追加を使用してください。10.8.2 で公開 API・スキーマ・既定値の変更や移行要件はありません。

## Cold な初回クエリが SafeWindow 全体を待つ — 10.8.1 で解決 (SEK-G21)

**症状**。書き込み直後、cold な multi-projection activation への初回クエリが、自身の catch-up log では
`CurrentPosition == TargetPosition == head` なのに、ほぼ `SafeWindow` 全体にわたり fail-closed になり得ました。

**原因**。初回クエリ barrier は正しく safe/restored チェックポイントから開始していましたが、完了判定にも
safe 位置を使っていました。そのため SafeWindow 内のイベントは graduation まで reached と判定されませんでした。

**修正**。G14 poison を再読するため START は safe/restored チェックポイントのままです。REACHED はその in-call
read 自身が返した権威 cursor のみを使い、共有 timer 進捗を証拠にしません。cold な state・snapshot・scalar・list
クエリは、自身の catch-up が固定 head に到達すれば最新の `IsSafeState=false` 結果を返せます。真の short read と
read failure は引き続き fail-closed かつ retryable で、poison fault、G18 rebuild、G20 tombstone/CAS、公開 API、
schema、SafeWindow 設定は変更しません。

## イベントの `ExecutedUser` が "GeneralSekibanExecutor" のまま — SEK-G23 で解決

**症状**。ストア内のすべてのイベントで `ExecutedUser = "GeneralSekibanExecutor"` になっており、API が知っている呼び出し元のIDが反映されません。

**原因**。コマンド経路は、明示的に提供しない限り HTTP コンテキストや呼び出し元の識別子にアクセスできません。

**修正**。`IExecutedUserProvider` を実装して DI に登録してください。コマンド経路はコマンドごとに 1 回だけ評価し、そのコマンドが生成するすべてのイベントの `EventMetadata.ExecutedUser` に書き込みます。プロバイダーが未登録、または `null`/空文字を返した場合は、従来の既定値である `"GeneralSekibanExecutor"` にフォールバックします。シリアライズ/WASM コミット経路は常に `"SerializedSekibanExecutor"` を使用します。

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IExecutedUserProvider>(sp =>
    new HttpContextExecutedUserProvider(sp.GetRequiredService<IHttpContextAccessor>()));
```

> **ライフタイムの指針。** executor はプロバイダーをキャプチャします。scoped または transient のプロバイダーを使う場合は、executor も scoped または transient で登録してください。アンビエント HTTP コンテキスト方式ではプロバイダーが singleton なので、executor も singleton にできます。

プロバイダーはすべての実行ファサードのコンストラクタで省略可能な引数なので、既存の呼び出し元に変更は不要です。

## ホスト時刻の巻き戻り後にイベントが見えなくなる — 10.13.0 で解決 (SEK-G31)

**症状**。イベントのコミットは成功したのに、`> checkpoint` で増分読み取りする projection がそのイベントを取得できません。
writer の UTC 時刻が、最後に永続化された `SortableUniqueId` の tick prefix より前へ戻ると発生し得ます。

**修正**。すべての本番 executor 書き込み経路は、DI singleton の単調増加 generator を共有します。正規化済み service id
ごとの最初のイベント生成操作より前に、その service の永続 head を読み、論理 tick floor を原子的に seed します。
割り当ては `max(TimeProvider.GetUtcNow().UtcTicks, previous + 1)` を使用し、既存の fresh Guid 由来 suffix と30桁 wire format
を維持します。head 読み取り失敗は予約・割り当て・書き込みより前に retryable な型付きエラーとなり、wall-clock へ
fallback しません。

**保証境界**。これは store-aware な Sekiban executor 経由の書き込みを、単一プロセス内および再起動をまたいで保護します。
分散 sequencer ではありません。異なるホスト間で想定する最大 clock skew に合わせて projection の `SafeWindow` を設定して
ください。`IEventStore` へ直接書き込み、独自に id を作る呼び出し元は、この順序保証を自身で負います。

**WSL2**。時刻が繰り返し飛ぶ場合は Hyper-V と `systemd-timesyncd` の競合を確認してください。WSL を更新し、Windows 側で
`wsl --shutdown` を実行して distribution を再起動し、意図した時刻同期方式だけが有効であることを確認します。単調増加
allocator は同一ホストの巻き戻りによるイベント欠落を防ぎますが、継続的な clock drift の是正は運用上必要です。

**10.13.0 リリースノート**。DCB executor が生成する `SortableUniqueId` はホスト時刻の巻き戻り下でも単調増加し、再起動後は
service ごとの store head から lazy seed されます。従来の公開 constructor と static signature、30桁 format、random suffix
semantics は互換のままです。

## verify-only の materialized-view 起動と SQL policy — dcb-v10.14.0 (SEK-G32)

`MvInitializationMode.VerifyOnly` は BYO database 用の明示的な経路です。version 付きの宣言的 schema contract と事前に
用意した registry binding を read-only provider inspector で検証します。infrastructure ensure、projector 初期化、fallback DDL、
registry row の自動 seed は行いません。4 provider の inspector は catalog read を使い、SQLite verifier は read-only metadata 経路で
通常の session PRAGMA を実行しません。SQL Server は `MvOptions.SqlServerInspectionConnectionString` に独立した最小権限の
inspection principal を要求します。DML/DDL の write permission を持たず、`VIEW DEFINITION` など契約に必要な非書込み
metadata visibility を確立できない場合は、catalog read 前に `UnsupportedProviderCapability` の型付き failure を返します。
standalone instance の強制能力として `ApplicationIntent=ReadOnly` は使いません。

`MvSqlStatementPolicyMode.Enforced` は opt-in です。initialization/apply batch の全件を provider 実行前に preflight し、rows・single-row・
scalar query port も gate します。projector への raw connection / transaction 公開を止めますが、`Legacy` は互換既定値のままです。
policy の未登録・throw・invalid・deny は typed safe reason 付きで fail-closed し、cancellation は cancellation のままです。hosted worker
は verification を retry し、schema ensure へ fallback しません。
