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
する control がその ceiling を超えることを確認します。143 MiB fixture は週次/manual schedule でのみ timeout と
virtual-memory ceiling 付きで実行します。elapsed time、peak RSS、選択された capability path、read count、buffer counter
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

既存の hot-only `event_count_checkpoint` trigger が最初に評価されます。追加 fallback は
5,000 fetched events 到達時の `PersistReason=fetched_count_checkpoint` と、5 分経過時の
`PersistReason=time_checkpoint` です。cold read は既存の設定された segment・applied-count・
interval trigger を維持し、fetched-count fallback も使います。cold/hot の選択は read metadata
の `UsedCold` だけで決まり、`UsedCold=false` の hybrid store は hot-only の定数を使います。
summary では `PersistTriggered` (decision) と `PersistOutcome` (`durable_write`,
`no_durable_write`, `not_attempted`) を分けて報告します。trigger されたこと自体は、
durable checkpoint が commit された証明ではありません。

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
