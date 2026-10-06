# マルチプロジェクション - 合成リードモデル

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [コマンド・イベント・タグ・プロジェクター](03_aggregate_command_events.md)
> - [マルチプロジェクション](04_multiple_aggregate_projector.md) (現在位置)
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
> - [マテリアライズドビュー基礎](20_materialized_view.md)

タグプロジェクターがタグ単位の状態を構築するのに対し、マルチプロジェクションは複数タグを組み合わせた
読み取りモデルを生成します。Orleans では各マルチプロジェクションが専用の Grain で動作し、大きな状態は
Azure Blob Storage にスナップショットとして退避できます。

## 基本構造

`IMultiProjector<T>` を実装して、イベントとタグ状態を引数にプロジェクションを更新します。

```csharp
public class WeatherForecastProjection : IMultiProjector<WeatherForecastProjection>
{
    public static string MultiProjectorName => "WeatherForecast";
    public static string MultiProjectorVersion => "1.0.0";

    public static MultiProjectionState Project(
        MultiProjectionState current,
        Event currentEvent,
        IReadOnlyDictionary<ITag, TagState> tagStates)
    {
        // イベントと関連タグ状態を元にリードモデルを更新
    }
}
// internalUsages/Dcb.Domain/Projections/WeatherForecastProjection.cs
```

`GenericTagMultiProjector<TProjector, TTag>` のようなジェネリック実装を使うと、タグ一覧をそのままリスト表示する
投影を簡単に作れます (`internalUsages/Dcb.Domain/DomainType.cs`)。

## 状態のライフサイクル

1. Orleans ストリーム経由でイベントを受信
2. 対象タグの最新状態を `TagStateGrain` から取得
3. プロジェクターで状態を更新
4. 必要に応じて `IBlobStorageSnapshotAccessor` を使い Blob Storage にスナップショットを保存

実装詳細は `src/Sekiban.Dcb.Orleans/Grains/MultiProjectionGrain.cs` を参照してください。

## 受動的なプロジェクション状態 (SEK-G24 / dcb-v10.10.0)

フリート監視は、プロジェクション Grain を起動せずにキャッチアップ状況をサンプルできます。プロバイダーの
登録時に `IProjectionStatusReader` と `ISerializedProjectionStatusReader` が登録されるため、次のように読み取れます。

```csharp
var reader = serviceProvider.GetRequiredService<IProjectionStatusReader>();
var result = await reader.ReadAsync(new ProjectionStatusReadRequest(ProjectorName: "WeatherForecast"));
```

各 `MultiProjectionGrain` は専用の 30 秒タイマーで best-effort の heartbeat を書き込みます。タイマーは
interleave、keep-alive 無効で、物理行は `(ServiceId, ProjectorName, ProjectorVersion, ClusterId)` の 1 行です。
`ActivationId` は行のデータとして保持するため、replacement activation が別行を作ったり sequence fence を
迂回したりしません。ストレージ書き込みは独立した timeout と上限付きバックオフを使い、同じ失敗のログを
レート制限します。状態書き込みによってプロジェクション処理を止めません。reader は service ごとに
sampling window（既定 5 秒）あたりイベント総数の分母を 1 回だけ取得し、bounded parallelism で異なる
`LastTraversedSortableUniqueId` ごとに後続イベント数を数えます。filtered event も traversed cursor に含むため、
`AppliedEventCount` が小さくても `RemainingEventCount` が 0 になり得ます。`IsCaughtUp` はさらに fresh な lease、
非 fault、fresh cluster conflict なしを要求します。サンプルには `SampledAtUtc` と `Consistency == "bestEffort"` が付き、
atomic な head/count を主張しません。

運用上の status は 3 層で使い分けます。(1) フリート全体の catch-up 概要には Grain を起動しない passive
registry、(2) restore/checkpoint の詳細には永続化 snapshot API、(3) authoritative な最新の projection 結果が
必要な場合には Grain query を使います。既存の snapshot API と Grain の status API は変更されません。

Cloud/WASM の転送には既存の `ISerializedSekibanDcbExecutor` ではなく、新しい
`ISerializedProjectionStatusReader` の V1 envelope を使います。serialized 境界の `ServiceId` はサーバー側の
`IServiceIdProvider` から決まり、クライアントが別サービスを選ぶことはできません。ホスト側の endpoint は
operator 専用として既定で deny し、必要な場合だけ明示的な認可ポリシー
(`RequireAuthorization("ProjectionStatusOperator")` など) を要求して公開してください。
`AllowAnonymous` は使わないでください。

## スナップショット退避

`Sekiban.Dcb.BlobStorage.AzureStorage` を利用すると大規模な状態を Blob Storage に退避できます。

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
    new AzureBlobStorageSnapshotAccessor(
        sp.GetRequiredKeyedService<BlobServiceClient>("MultiProjectionOffload"),
        "multiprojection-snapshots"));
```

`MultiProjectionGrain` がアクセサを検出すると、定期的にスナップショットを保存しメモリ使用量を抑えます。

### スナップショット Blob のプレフィックス

このソースリビジョンの SEK-G120 修正以降、設定したプレフィックスは新しく書き込むすべてのスナップショット Blob に適用されます。seekable stream のキーは `{prefix}/{projector}/{sha256}.bin` です（行スナップショットの `{projector}` にはバージョンも含まれます）。プレフィックスが null または空なら従来と同じキーになります。プレフィックスから取り除くのは末尾の `/` のみです。non-seekable stream のプレフィックス付き GUID キーは従来どおりです。

`df24f127` 以降の seekable stream の決定的キーにはプレフィックスが付きませんでした。その Blob は古いキーのまま残り、移行なしで引き続き読み取れます。non-seekable stream の書き込みや同コミット以前の書き込みには、すでにプレフィックスが付いている場合があります。アップグレード後は同じ内容がプレフィックスの下にもう一度保存されます。プレフィックスで一覧取得・削除する場合も、`OffloadKeyEnumerator` と参照キーの安全規則を使い、古いプレフィックスなしのキーを考慮してください。スナップショット Blob は、同じ設定のプレフィックスを使う cold-event オブジェクト `{prefix}/control/...`、`{prefix}/segments/...` と並んで配置されます。

プレフィックスは短くし、先頭に `/` を付けず、区切りには `/` のみを使ってください。プロバイダーの命名規則も適用されます。プレフィックス付きの決定的キーが 512 文字を超えると、アップロード前に `InvalidOperationException` が発生します。プレフィックスが 57 文字以下なら、PostgreSQL の有効な projector 名・バージョンの上限（256・128 文字）の範囲で必ずこの制限に収まります。プレフィックスが null または空の場合、新たな長さチェックは行いません。

### 参照されている退避キーの列挙

スナップショットの blob 参照には二つの形式があります。行の `IsOffloaded` / `OffloadKey` / `OffloadProvider` と、`SerializableMultiProjectionStateEnvelope` 内の `OffloadedState.OffloadKey` / `StorageProvider` です。行自体が退避されている場合、その blob にエンベロープが入っているため、両方の参照を保持する必要があります。現在の writer はエンベロープを通常の JSON として保存します。gzip 対応は読み取り側の許容のみです。Orleans の `MultiProjectionGrainState` は退避キーを保持せず、使われなくなった旧 `SerializedState` フィールドはクリアされるだけです。参照元はストアの行です。

Core の追加 API `Sekiban.Dcb.Snapshots.OffloadKeyEnumerator` を使用します。

```csharp
await foreach (var reference in OffloadKeyEnumerator.EnumerateAsync(
    store, domainTypes.JsonSerializerOptions, cancellationToken))
{
    // Kind: Row、Envelope、または Undecodable。
    // Lifecycle: 取得できれば Active / Tombstoned、それ以外は null。
    // キーの和集合を収集し、Undecodable が一つでもあれば削除を中止する。
}
```

このヘルパーは tombstone を含む、一覧にある全 projector version を走査し、行の blob を解決してからエンベロープを調べます。標準以外の命名ポリシーを使う場合は、アプリケーションの `JsonSerializerOptions` を渡してください。`InlineState` をデシリアライズせず前方のみ読み進めますが、デコーダーのバッファのピークメモリ使用量は、単一の最大 JSON トークンとその直前の意味を持たない空白（プロパティの場合は名前、コロン、および値の開始位置までの周囲の空白を含む）のサイズの約2倍に、小さな定数分を加えた量です。実際の Sekiban の書き込み処理はコンパクトな JSON を出力します。この上限はデコーダー自身のバッファだけが対象であり、プロバイダーはレコードの読み取り時に inline の `StateData` byte[] など、状態データ全体をメモリに展開する場合があります。行の検索、ストリームの取得、デコードに失敗すると `Detail` 付きの `Undecodable` を返します。一覧取得の失敗と呼び出し元のキャンセルは伝播します。checkpoint slot が存在しない、または読めない場合は `Lifecycle` が null となり `Detail` に理由を記録しますが、キーは引き続き返します。

列挙は **観測** です。キーが出力されなかったことだけでは削除を許可できません。一覧取得が結果整合性のストアもあり（例: Dynamo の `ListAllAsync`）、正常終了しても行を見落とす可能性があります。走査はストアの現在の ServiceId に限定され、blob キー自体には ServiceId が含まれません。同じ blob コンテナを共有する ServiceId ごとに一度ずつ実行してください。

安全な外部 GC には、以下のすべてが必要です。

- 全サービス、全バージョンのどの行からでも参照されているキーを削除しないこと。tombstone の行も対象です。出力は和集合として扱ってください。version rewrite はキーをコピーし、内容アドレス方式は重複を排除するため、複数の行が同じキーを参照できます。
- `Undecodable` が一つでもあれば **何も削除しない** こと。一覧取得と検索の間に削除された行も `Undecodable` となります。通常は再実行で解消します。
- 完全で整合性のある参照ビューと猶予期間を確保すること。blob は行のコミットより先にアップロードされ、CAS 失敗時には孤立した blob が残る場合があります。
- 削除時に参照を再確認し、並行アップロードとコミットを保護する協調制御または条件付き削除を使うこと。内容アドレス方式のキーは GC の確認と削除の間に writer が再アップロードできます。再確認だけではこの競合を防げません。

削除 API と協調プロトコルは #1253 item 3 の範囲であり、**ここでは提供しません**。

### 退避済みスナップショットのストリーミング復元

退避済みスナップショットを復元する際、Sekiban は Blob ペイロードを 1 回だけ開き、その非 seekable stream を
resolver と actor を通して projector registry まで渡します。組み込みの reflection JSON / AOT JSON registry はこの
経路を使います。custom projector は加法的な `ICoreMultiProjectorWithStreamDeserialization` を実装することで
projector 単位で opt-in できます。registry 側は別インターフェース `IStreamingMultiProjectorTypes` で capability を
公開します。`ICoreMultiProjectorTypes` 自体は変更されないため、既存の外部 registry も引き続き利用できます。

保証は意図的に限定されています。capability をサポートする projector の **退避済み** スナップショットでは、復元時に
完全な非圧縮ペイロードの長さに比例する追加の連続 `byte[]` / `string` を materialize しません。projection graph
そのものは必要で、こちらがメモリ使用量の大半になる場合があるため、これは **no-OOM の保証ではありません**。
Sekiban は independent な safe/unsafe restore graph を作るためだけに一時ファイルを使いますが、payload 全体の managed
buffer は作りません。save 側の streaming 化と圧縮形式の変更は、この restore の保証の範囲外です。

| スナップショットと registry の条件 | 復元時の動作 | 非 buffering 経路の保証 |
| --- | --- | --- |
| 退避済み payload + capability あり | 開いた stream を projector に渡し、非同期読み取りと現在位置を使う。reflection/AOT JSON は gzip と raw の legacy JSON を受け入れる。 | あり |
| 退避済み payload + custom projector が stream capability を実装 | custom projector が caller 所有の stream を受け取る。 | custom 実装が契約を守る範囲であり |
| 退避済み payload + capability なし | 1 回だけ observable な compatibility fallback が payload を buffer し、projector / registry / `Format=offloaded` / `Reason=capability-absent` を log する。payload 内容は log しない。 | なし |
| 退避済み payload + capability はあるが open/read/decompress/deserialize が失敗 | 元の failure を返す。buffering retry は **0 回**。 | restore は成功せず fail-closed |
| inline JSON/Base64（legacy v9/V10 inline envelope を含む） | 互換性のため既存の inline restore は buffer を使う。 | なし — inline Base64 はこの保証の明示的な対象外 |

stream 実装は非同期 read を使い、`CancellationToken` を尊重し、現在位置からの非 seekable partial-read stream を
サポートし、stream を dispose してはいけません。dispose の責任は resolver caller にあります。stream restore の最中は、
state query・event apply・promotion・compaction・snapshot persistence は old / partial payload や tracking metadata を
publish する代わりに失敗します。terminal な restore failure により既に publish 済みの payload または tracking
metadata が残る場合、この fail-closed barrier は latch されたままです。以前の checkpoint は query、apply、catch-up、
promotion、persistence に利用できません。初回 restore が失敗しただけであれば serve すべき以前の payload はないため、
legacy の empty-state/rebuild path を維持します。後続の restore/rebuild attempt 自体は許可され、atomic な restore が
成功した場合だけ latch 済み barrier が解除されます。それ以外では host は stale state を serve せず通常の
recovery/catch-up policy に従います。

#### Restore caller inventory

| Caller | Snapshot 形状 | 経路 |
| --- | --- | --- |
| `MultiProjectionGrain` → `NativeProjectionActorHost` → `NativeProjectionSnapshotHandler` | Orleans の state-store activation。incident/OOM の本番 entry point | outer state stream を開き、`SnapshotEnvelopeResolver.ResolveForRestoreAsync` を呼び、その後 `GeneralMultiProjectionActor.SetResolvedSnapshotAsync` を await する |
| `MultiProjectionStateBuilder.LoadRestoreAsync` | offline/builder checkpoint restore | outer envelope を deserialize し、退避済み payload stream を resolve して同じ actor seam を await する |
| `NativeMultiProjectionProjectionPrimitive.ApplySnapshot` | inline primitive snapshot | `SetSnapshotAsync` を呼ぶ。inline compatibility path のみ |
| `GeneralMultiProjectionActor.SetCurrentState` / `SetCurrentStateIgnoringVersion` | direct legacy inline state | buffered compatibility path のみ |
| `SnapshotEnvelopeResolver.ResolveInlineAsync` | explicit compatibility adapter | caller が inline envelope を明示的に要求したためだけに materialize する。本番の offloaded restore では `ResolveForRestoreAsync` を使う必要がある |

通常の DCB test suite には、小さな graph と 16–32 MiB の offloaded gzip wire を組み合わせた制御 fixture があります。
これは production aggregation counter と、supported stream seam に whole-payload aggregation API が入ることを拒否する
structural guard を併用します。別の **DCB Streaming Restore
Memory Smoke** workflow は、その制御 fixture も allocation ceiling 付きの独立 process で実行し、意図的に buffering
する control がその ceiling を超えることを確認します。143 MiB fixture は週次/manual schedule でのみ、独立した
process で timeout 付きで実行します。elapsed time、peak RSS、選択された capability path、read count、buffer counter
を記録し、full-payload materialization path がないことを評価します。OOM が不可能だという主張ではありません。

## 整合性のポイント

- イベントはグローバル順序で届くため、`IWaitForSortableUniqueId` を活用すると最新データを保証できます。
- プロジェクターは純粋関数（副作用なし）である必要があります。
- バージョンを更新したら `MultiProjectorVersion` を必ず変更し、リビルドを促してください。

## 代表的な用途

- ダッシュボードの集計
- Blazor UI 用の一覧ビュー
- 複数タグを跨ぐ統計情報やランキング

例: `internalUsages/Dcb.Domain/Student/StudentSummaries.cs` は複数タグから学生サマリーを組み立てています。

## マルチプロジェクションとマテリアライズドビューの違い

Sekiban には現在、2 種類の読み取りモデルがあります。

- **マルチプロジェクション**: Orleans Grain 内に保持されるメモリ状態。`ISekibanExecutor.QueryAsync` と自然に接続されます。
- **マテリアライズドビュー**: 同じイベントストリームから更新される DB テーブル。SQL の一覧取得、レポート、外部参照に向きます。

Sekiban 内部だけで完結する読み取りならマルチプロジェクション、リレーショナル DB として見せたいなら
マテリアライズドビューが適しています。詳細は [マテリアライズドビュー基礎](20_materialized_view.md) を参照してください。

## デュアルステートの収束とセーフウィンドウ昇格 (SEK-G18)

マルチプロジェクションは 2 つの状態を保持します。**safe** 状態(セーフウィンドウより古い
イベントをグローバルな `SortableUniqueId` 順で反映)と、**served/unsafe** 状態(クエリが返す値)です。

`Project` は入力 payload を不変として扱い、新しい payload インスタンスを返す必要があります。既存の
projector が意図的に入力を変更して同じインスタンスを返す場合、payload に `IMutatesProjectionInput` を
実装し、`GenerateInitialPayload` も呼び出しごとに新しいインスタンスを返してください。この marker により、
dual-state wrapper は snapshot serializer を使って safe baseline を分離します。catch-up または rebuild 中に
safe なイベントを順序どおり反映した直後を含め、reconcile のたびに safe から独立した clone を生成します。
したがって各 reconcile のコストは O(state size) です。順序どおりの unsafe fold は、すでに独立している served
instance に適用され、追加の clone は行いません。この opt-in は throughput と引き換えに安全性を得るものです。
大きな state では、入力を変更しない immutable または copy-on-write projector を推奨します。
第 1 parameter に `DcbDomainTypes` を受け取る公開 `DualStateProjectionWrapper<T>` constructor と
`DualStateProjectionWrapperFactory.CreateWithDomainTypes` は、登録済み snapshot serializer で初期 marker payload を
一度 clone します。`DcbDomainTypes` を受け取らない従来の overload は serializer に即した分離を保証できないため、marker
payload に対して fail-fast します。non-marker payload では従来どおり `System.Text.Json` を使用します。運用時に
未宣言の入力変更を検出するには `GeneralMultiProjectionActorOptions.VerifySafeStateIsolation` を一時的に有効化
できます。unsafe fold の前後で safe payload を serialize し、その byte 列を比較するため、既定値は無効です。
serialization は決定的である必要があり、順序なし collection など出力 byte 順が変動し得る payload では、論理値が
変化していなくても false positive が発生する可能性があります。

marker payload では、`GetUnsafeProjection` または `GetUnsafeProjectorPayload` が返す served instance は、その後の
順序どおりの unsafe fold によってその場で変更されます。呼び出しをまたいでその instance を保持しないでください。

- **served 状態は到着順ではなく再構成される。** セーフウィンドウ昇格のたびに、served 状態は
  `safe ベースライン + まだバッファ中のイベントをグローバル SortableUniqueId 順で再適用` として
  導出し、原子的に公開します。順序が入れ替わって到着した 2 イベント(例: インスタンス間の重複
  create)でも、全インスタンスが同じ結果に収束します。first-event-wins プロジェクタでは、ローカル
  到着順に関係なくグローバルに最も早いイベントが勝ちます。
- **`IsSafeState` は真実を表す。** served 状態が safe 状態と同一に公開された場合(バッファが空で
  再構築保留なし)にのみ `true` です。タイムスタンプ比較のみで決めることはありません。`IsSafeState=true`
  を返すクエリは、再構成済みのグローバル順の値であることが保証されます。
- **順序違反時の再構築(fail-closed)。** 保持している safe ヘッドに対してイベントがグローバル順から
  外れて safe に昇格した場合、増分(圧縮済みベースライン)経路では並べ替えできません。その場合は
  **権威イベントストアから初期状態を起点にした完全な順序付き再構築**を行います。再構築中は
  すべての state/scalar/list クエリが再構築バリアを待ち、再構築後のペイロードで応答するか fail-closed で
  失敗します。古い値で success を返すことはありません。G14 フォルト経路は再構築自体の失敗のために
  予約されています。

### チェックポイント復元の厳密性 (SEK-G18 / #1086)

- **キャッチアップ開始位置は権威的。** チェックポイント復元後、キャッチアップはチェックポイント
  レコードの `LastSortableUniqueId` から、その位置を**排他的**に読み取って開始します。id が
  チェックポイント位置と等しいイベントは復元済みペイロードに既に反映されているため再読み込みされず、
  二重カウントや再適用は起きません。(すべてのイベントストア — Postgres/SQLite/Cosmos/DynamoDB、
  インメモリ、Hybrid の cold→hot 引き継ぎ — は厳密な `SortableUniqueId > since` フィルタを使用します。)
- **`EventsProcessed` は永続的な safe チェックポイント数**で、整合性シグナルとして使用します。復元は
  これをベースラインとし、新規イベントゼロの再起動では同一の payload/position/threshold/count を復元します。

### Catch-up の永続化 cadence と telemetry (SEK-G37 / #1142)

Catch-up の完了判定は `FetchedCount == 0` のみです。読み取ったイベントがすべて
filter されて `AppliedCount == 0` になった場合も、traversal cursor を進め、適用済み
batch と同じ progress・persist decision・telemetry の共通 seam を通ります。これにより
filter 済み tail が checkpoint fallback より前に catch-up を終了させません。

Hot-only checkpoint cadence は `GeneralMultiProjectionActorOptions` で設定できます
(SEK-G89、関連 issue #1253)。

- `HotCatchUpPersistMaxFetchedEvents` の既定値は `5000` です。累積適用イベント数の
  modulo (`event_count_checkpoint`、最初に評価) と、前回の永続化からの読み取り数
  (`fetched_count_checkpoint`、2 番目に評価) の両方に使われます。
- `HotCatchUpPersistMaxIntervalSeconds` の既定値は `300` です。経過時間の fallback
  (`time_checkpoint`、最後に評価) を制御します。
- 値が ≤ 0 なら対応する trigger を無効にします。`ProjectorPersistenceOverrides[projectorName]`
  の `MultiProjectionPersistenceOverrideOptions` にある同名の nullable プロパティで
  projector ごとに上書きできます。null はグローバル値を継承します。

読み取り数の window は永続化の試行ごとにリセットされますが、適用数は累積値です。
読み取り数と適用数が異なると両 trigger の位相がずれ、実効 cadence が設定イベント数の
約半分になる場合があります。しきい値を増やすと full-state snapshot/blob の書き込みと
storage I/O を減らせますが、中断後に再実行するイベント数が増えます。両方を無効にすれば
完了時のみの checkpoint にできます。catch-up に新しいイベントがあれば、完了時の最終
永続化は引き続き実行されます。既定の 5,000 イベント / 5 分の動作は変わりません。
これらの設定は live-path の `PersistBatchSize` と `PersistIntervalSeconds` とは独立しています。
hot checkpoint の永続化ごとに safe history と保持中のコレクションも圧縮されるため (`CompactSafeHistory` / `CompactRetainedCollections`)、しきい値を増やす、または trigger を無効にすると、次の checkpoint または完了まで catch-up のメモリ使用量が増え続けます。

cold read は既存の設定された segment・applied-count・interval trigger と fetched-count
fallback を維持します。cold/hot の選択は read metadata の `UsedCold` で決まり、
`UsedCold=false` の hybrid store は hot-only 設定を使います。summary では
`PersistTriggered` (decision) と `PersistOutcome` (`durable_write`, `no_durable_write`,
`not_attempted`) を分けて報告します。trigger 自体は durable checkpoint の commit の証明ではありません。

### 初回クエリ catch-up の位置契約 (SEK-G21 / 10.8.1)

Orleans の fresh activation は、最初の state・snapshot・scalar・list クエリの前に fail-closed
barrier を置きます。この barrier は意図的に異なる 2 種類の位置を使います。

- **START** は safe/restored チェックポイントです。復元レコードの `LastSortableUniqueId` は、
  background と in-call catch-up が共有する単一の内部 resolver から一度だけ lease されます。
  これにより SafeWindow 内の poison を含む未チェックポイント tail 全体を再読します。
- **REACHED** は、その in-call event-store read 自身が返した権威 cursor です。safe 位置でも、
  timer と共有する進捗値でもありません。自身の read が固定 head に到達すれば、cold な初回
  クエリは safe-window graduation を待たず、最新の unsafe state を直ちに返せます。

固定 head に届かない short read は引き続き fail-closed で retryable です。read failure は元の
例外を保持します。safe チェックポイント、SafeWindow の動作、公開 API、storage schema は変更しません。

### 初回クエリの待機時間を制限する設定 (SEK-G90; #1253 item 5)

`GeneralMultiProjectionActorOptions.FirstQueryCatchUpMaxWaitMs` の既定値は `0` です。
0 以下では従来の blocking barrier を維持します。正の値（例: `1000`）で、
クエリ受付後の gate 待機を N ms に制限します。
`ProjectorPersistenceOverrides[projectorName].FirstQueryCatchUpMaxWaitMs` は nullable override です。
null は全体設定を継承し、0 以下は当該 projector の blocking 動作を復元します。
N は Orleans `ResponseTimeout` と HTTP/client timeout より十分短く設定してください。
activation と request queue の時間は含まれず、応答全体の上限ではありません。

この設定を有効にすると、activation 後の初回クエリは activation の background catch-up を共有し、
その実行が完了してから gate が settle します。既定では catch-up interval（1 秒）ごとに
`MaxConsecutiveEmptyBatches`（5）回の空 batch を確認して完了します。このため短い tail でも
settle まで約 5 秒かかる場合があり、既定の in-call path ではミリ秒で完了します。
N はこの遅延を考慮して設定してください（例: 上記 timeout の範囲内で 10 秒以上）。
N が小さい場合は初回に「catching up」応答が返ることを想定してください。この遅延の短縮は後続課題です。

空のイベントストアに対する新しい activation でも、N が小さい場合は idle settle が完了するまで
catching-up エラーが返ることがあります（約 `MaxConsecutiveEmptyBatches` × catch-up interval）。
同時に送った poll は non-reentrant grain により直列化され、各 poll は受付後に最大 N ms 待機します。

対象は durable rebuild marker が pending でない activation の generic arm のみです。
snapshot restore 失敗後に host を再作成してから行う activation generic arm も対象です。
checkpoint tombstone は SEK-G85、durable marker は SEK-G18 の動作を維持します。
checkpoint mutation、operator reset/rebuild、activation 中の host recreation の re-arm は
blocking のままです。re-arm は現在の bounded episode を終了します。他の arm への拡張は後続課題です。

クエリは activation の進行中 timer catch-up を共有し、必要なら incremental restart
(`forceFull: false`) と background gate settlement を開始します。対象クエリの中で
full replay は実行しません。timer は interleave しますが、全体の同時実行制限で batch が
skip される可能性があるため N 内の進捗は保証されません。gate が完了すれば成功し、
未完了なら state/snapshot は `ResultBox.Error`、scalar/list は `InvalidOperationException` を返します。
メッセージは `MultiProjectionQueryFailClosedMessages.CatchUpInProgressPrefix`
(`Projection catch-up is in progress:`) で始まり、現在位置・target 位置・処理イベント数を含みます。
background 完了後のクエリは成功します。待機中に検出した live projection fault も SEK-G14 が優先します。
fail-closed クエリは `LastError` を変更しませんが、実際の background failure は従来どおり更新します。

`GetStatusAsync` の `FirstQueryCatchUpPending`、`CatchUpTargetPosition`（初回 batch 前は null 可）、
`LastBackgroundCatchUpError`（message と UTC timestamp）で状態を確認できます。
background error は episode generation に限定され、settle が成功した後も次の arm まで status に残る場合があります。
catching-up メッセージにも含まれるので障害を把握できます。
追加 status constructor 引数には既定値があります。
`MultiProjectionQueryFailClosedMessages.RebuildPendingPrefix` (`Projection rebuild is pending:`) は
SEK-G18 と SEK-G85 共通です。後者は `checkpoint tombstone` で区別できます。

SEK-G21 の authoritative start position 契約は維持します。background settle の head read は
クエリ到着前の場合があり、成功時には通常の projection lag semantics が適用されます。
受付時点の freshness は保証されません。既存の 30 秒 `waitForCatchUp` helper は変更しません。

定数は grain が生成するメッセージの先頭を表します。transport や ResultBox の wrapper が
文字列を前置するため、host は `Contains(prefix, StringComparison.Ordinal)` で判定します。
catch-up が非アクティブで initiation と settlement が進行中でなければ、poll は共有 background
Ensure を直ちに開始します。Refresh 済みの host は timer の空 batch 閾値を待たずに N 内で
settle できます。クエリ内で Ensure を inline 実行することはありません。

ASP.NET Core host の 503 と Retry-After マッピング例（error の取得元は query surface に合わせます）:

```csharp
using Sekiban.Dcb.Orleans.Grains;

// Map both ResultBox.GetException() and thrown scalar/list query exceptions.
static IResult MapProjectionError(Exception error, HttpResponse response)
{
    if (error.Message.Contains(MultiProjectionQueryFailClosedMessages.CatchUpInProgressPrefix,
            StringComparison.Ordinal) ||
        error.Message.Contains(MultiProjectionQueryFailClosedMessages.RebuildPendingPrefix,
            StringComparison.Ordinal))
    {
        response.Headers["Retry-After"] = "2";
        return Results.Problem(statusCode: 503, detail: error.Message);
    }
    return Results.Problem(statusCode: 500);
}
```
