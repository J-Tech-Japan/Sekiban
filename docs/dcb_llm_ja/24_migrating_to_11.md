# DCB 11 への移行

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [ストレージプロバイダー](11_storage_providers.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [DCB 11 への移行](24_migrating_to_11.md) (現在位置)

このページは、DCB 10.x（10.25.0）から DCB 11 系（最初の版は `11.0.1-preview01`）へ移行するためのガイドです。

11 系では、`ResultBox` を使うのは WithResult の表面だけになりました。`ResultBoxes` に依存するパッケージは
`Sekiban.Dcb.WithResult`、`Sekiban.Dcb.WithResult.Model`、`Sekiban.Dcb.Orleans.WithResult`、
`Sekiban.Dcb.WithResult.Testing` の 4 つです。Core、Core.Model、Core.Testing、すべてのストレージプロバイダー、
Orleans.Core、MaterializedView、WithoutResult 系のパッケージは例外ベースです。

変換は次の 4 つのルールに従っています。

1. **失敗は、これまでと同じ型・同じメッセージの例外になります。** 10.x で失敗の `ResultBox` に入っていたものが、
   そのまま送出されます。
2. **想定内の結果は値で返します。** 「見つからない」「イベントがない」「削除対象がない」は、エラーではなく
   `null`・`false`・空文字列・空の状態です。
3. **キャンセルは引き続き `OperationCanceledException` です。**
4. **契約は形だけが変わります。** `Task<ResultBox<T>>` は `Task<T>` に、`ResultBox<T>` は `T` になります。メンバー名と
   引数は変わりません。

イベント、タグ、スナップショット、永続化された Orleans の grain 状態は変わっていません。データの移行は不要です。

## 読むべき節

| あなたの状況 | 読む節 |
|---|---|
| `Sekiban.Dcb.WithResult` でコマンド・プロジェクター・クエリを書いている | [WithResult を使っている場合](#withresult-を使っている場合)、[再ビルドと依存関係](#再ビルドと依存関係) |
| `Sekiban.Dcb.WithoutResult` で書いている | [WithoutResult を使っている場合](#withoutresult-を使っている場合)、[再ビルドと依存関係](#再ビルドと依存関係) |
| `IEventStore` などのストレージ契約を実装している | [独自ストレージプロバイダー](#独自ストレージプロバイダー) |
| `ICoreMultiProjectorTypes`・`ITagProjectorTypes`・`ITagStatePayloadTypes`・`ICoreQueryTypes` を実装している、またはコア契約を直接呼んでいる | [独自の `I*Types` とコア契約の実装](#独自の-itypes-とコア契約の実装)、[コアランタイムの契約](#コアランタイムの契約) |
| Orleans を使っている | [Orleans](#orleans) |
| `MvExecutorBase` を継承している | [MaterializedView](#materializedview) |
| 全員 | [形以外の動作の変更](#形以外の動作の変更) |

## 再ビルドと依存関係

- **対象フレームワーク。** 11 系のパッケージは `net10.0` と `net11.0` を対象とします。`net10.0` の依存バージョンは
  10.25.0 と同じです。`net11.0` では `Microsoft.Extensions.*` と `Microsoft.Data.Sqlite` が `11.0.0` で、
  `Sekiban.Dcb.Postgres` は `Npgsql.EntityFrameworkCore.PostgreSQL` `11.0.0-rc.3` と Entity Framework Core
  `11.0.0-rc.3.26522.148` を使います。`Microsoft.Orleans.*` はどちらも 10.4.2 です。
- **すべて再ビルドしてください。** 10.x に対してコンパイルしたアセンブリは 11 とバイナリ互換ではありません。
  `Sekiban.Dcb.*` を参照するプロジェクトはすべて再ビルドが必要です。たとえば `EventOrNone.None`・
  `EventOrNone.Event(...)`・`EventOrNone.EventWithTags(...)` は `EventOrNone` 本体から `Sekiban.Dcb.WithResult.Model`
  の C# 拡張メンバーに移りました（ソースは互換、バイナリは非互換）。古い Sekiban に対してコンパイルされたドメインの
  レジストリは `TypeLoadException` で読み込めません。
- **WithoutResult は `ResultBoxes` を推移的に持ち込まなくなりました。** WithoutResult のアプリ自身（ドメイン
  プロジェクトなど）が `ResultBox` の型を使っているなら、パッケージを直接参照してください。DCB のテンプレートは
  すでにそうしています。

  ```xml
  <PackageReference Include="ResultBoxes" Version="0.4.0" />
  ```

## WithResult を使っている場合

WithResult でドメインを書くための API は変わっていません。コマンドハンドラーは `Task<ResultBox<EventOrNone>>` を返し、
`IMultiProjector<T>.Project` は `ResultBox<T>` を返し、`IMultiProjectionQuery` / `IMultiProjectionListQuery` の
ハンドラーも、`ICommandContext` も、`ISekibanExecutor`（Orleans のエグゼキューターを含む）も、これまでどおり箱を
返します。`EventOrNone.None`・`.Event(...)`・`.EventWithTags(...)`・`SortableUniqueId.OptionalValue(...)` も、
`Sekiban.Dcb.WithResult.Model` の拡張メンバーとしてそのままコンパイルできます。

WithResult のインターフェイスは、例外ベースのコアのメンバーを明示的に実装し、あなたの箱を一度だけ開きます。
失敗した箱は中の例外としてコアに届き、コアの失敗は WithResult ファサードが元の例外のまま箱に入れます。

**失敗の箱に元の例外型が入るようになることがあります。** 10.x では、箱を返していた grain メンバーの失敗は
`Exception("[TypeName] message")` に平坦化されて届きました（`SekibanProjectionFaultException` だけは例外で、型と記述子を
保っていました）。11 では、Orleans がシリアライズできる限り、これらのメンバーは例外そのものを送ります
（[例外の転送](#例外の転送)）。`OrleansDcbExecutor` 経由では、次の 2 か所に表れます。

- `ICommandContext.TagExistsAsync` と `ICommandContext.GetTagLatestSortableUniqueIdAsync` は
  `ITagConsistentGrain.GetLatestSortableUniqueIdAsync` を読みます。grain の中で起きた失敗は、
  `Exception("[TypeName] message")` ではなく本来の型とメッセージのまま、ハンドラーの失敗の箱に入ります（たとえば
  `InvalidOperationException("Actor not initialized")` や、アクターのロック待ちでの失敗。WithoutResult では同じ例外が
  境界情報付きで送出されます）。Orleans のタイムアウトや通信エラーのような呼び出しの失敗は呼び出し側で発生するため
  10.x と同じで、イベントストアの失敗はタグアクターの catch-up が 10.x と同じく握りつぶすため、この経路ではどちらも
  変わりません。
- タグの予約は `ITagConsistentGrain.MakeReservationAsync` を呼びます。その失敗は引き続き
  `InvalidOperationException("Failed to reserve tags: ...")` にまとめられ、中のタグごとの文言が `[TypeName]` で
  始まらなくなることがあるだけです。

`[TypeName]` の接頭辞で判定していたコードは、例外型で判定するか、接頭辞に頼らないように変えてください。grain を直接
呼ぶコードは、例外を持った失敗したタスクを受け取ります（[Orleans](#orleans)）。

WithResult のコードにも影響しうるソースの変更:

- `SortableUniqueId.Parse(string)` は `SortableUniqueId` を返し、失敗すると `ArgumentException`（メッセージは同じ）を
  送出します。`TryParse` は変わりません。
- `ListQueryResultGeneral.ToTypedResult<T>()` は `ListQueryResult<T>` を、`QueryResultGeneral.ToTypedResult<T>()` は
  `T` を返します。
- `ConsistencyTag.SortableUniqueId` は `SortableUniqueId?` になりました（以前は `OptionalValue<SortableUniqueId>`）。
- `ICommandContext` は `ICoreCommandContext` を継承しなくなり、`GeneralCommandContext` も `ICoreCommandContext` を
  実装しなくなりました。
- `SimpleMultiProjectorTypes` がコアのレジストリから継承しているメンバー（`Project`、`GetProjectorVersion`、
  `GetProjectorType`、`GenerateInitialPayload`、`GetInitialPayloadGenerator`、`Serialize`、`SerializeToStream`、
  `Deserialize`、`DeserializeFromStreamAsync`）は値を返します。WithResult の `SimpleMultiProjectorTypes` の
  `RegisterProjectorWithCustomSerialization<T>()` は引き続き `ResultBox<bool>` を返します。
- コアの契約を直接呼ぶコード（[独自の `I*Types`](#独自の-itypes-とコア契約の実装)、
  [コアランタイムの契約](#コアランタイムの契約)、[独自ストレージプロバイダー](#独自ストレージプロバイダー)）は、相手が
  WithResult の型でも値を受け取ります。たとえば WithResult の `GeneralSekibanExecutor` は
  `Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(...)` を公開したままですが、
  `ISerializedSekibanDcbExecutor` 経由で呼ぶと `Task<SerializedCommitResult>` が返ります。

## WithoutResult を使っている場合

コマンドハンドラー、プロジェクター、クエリ、`ISekibanExecutor` の形は 10.x と同じです。変更点は次のとおりです。

- **シリアライズ済みエグゼキューターのメンバー**が箱ではなく値を返します。`GeneralSekibanExecutor`
  （`Sekiban.Dcb.Actors`、Sekiban.Dcb.WithoutResult）では:

  ```csharp
  // 10.x
  public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId)
  public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
      SerializedCommitRequest request,
      CancellationToken cancellationToken = default)
  public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsWithExpectedTagPositionsAsync(
      VersionedExpectedTagPositionSerializedCommitRequest request,
      CancellationToken cancellationToken = default)
  public Task<ResultBox<SerializedConditionalCommitResult>> CommitSerializableEventConditionallyAsync(
      SerializedConditionalCommitRequest request,
      CancellationToken cancellationToken = default)

  // 11
  public Task<SerializableTagState> GetSerializableTagStateAsync(TagStateId tagStateId)
  public Task<SerializedCommitResult> CommitSerializableEventsAsync(
      SerializedCommitRequest request,
      CancellationToken cancellationToken = default)
  public Task<SerializedCommitResult> CommitSerializableEventsWithExpectedTagPositionsAsync(
      VersionedExpectedTagPositionSerializedCommitRequest request,
      CancellationToken cancellationToken = default)
  public Task<SerializedConditionalCommitResult> CommitSerializableEventConditionallyAsync(
      SerializedConditionalCommitRequest request,
      CancellationToken cancellationToken = default)
  ```

  `OrleansDcbExecutor`（Sekiban.Dcb.Orleans.WithoutResult）も最初の 3 つが同じように変わりました。失敗は、ほかの
  WithoutResult のメンバーと同じ境界情報を付けて送出されます。
- **`SimpleMultiProjectorTypes.RegisterProjectorWithCustomSerialization<T>()` は `bool` を返します**（以前は
  `ResultBox<bool>`）。登録済みの場合も含めて `true` を返し、失敗すると元の例外を送出します（10.x は失敗の箱を返し、
  たいていは無視されていました）。`AotWithoutResultMultiProjectorTypes` は `bool` を返し、
  `NotSupportedException("Use RegisterProjector with JsonTypeInfo")` を送出します。
- **境界ガード。** 内部の `GuardedUnwrap` は `BoundaryGuard` になりました。Core が例外を送出するので、箱を開ける処理は
  もうありません。失敗はそのまま（同じインスタンス・型・スタック）再送出され、境界の情報は `Exception.Data` の
  `Sekiban.Boundary.Operation` と `Sekiban.Boundary.Target` に記録されます。キーは変わっていません。null の `Task` や
  null の結果は、これまでどおり `SekibanBoundaryException` になります。[よくある問題と解決策](13_common_issues.md)を
  参照してください。
- **`ResultBoxes` は持ち込まれなくなりました。** [再ビルドと依存関係](#再ビルドと依存関係)を参照してください。
- **アセンブリスキャンによる登録の失敗**は、`TargetInvocationException` ではなく元の例外として表に出ます。

## 独自ストレージプロバイダー

すべてのストレージ契約が 4 つのルールに従います。失敗の箱に入れていた例外を、そのまま送出してください。例外を
捕まえて箱に入れ直す必要はありません。`OperationCanceledException` はそのまま伝えてください。

### 見つからない場合と空の場合

| メンバー | 11 で該当なしのときの結果 |
|---|---|
| `IEventStore.ReadSerializableEventAsync` | `null`（10.x はプロバイダー固有の例外を持つ失敗の箱） |
| `IEventStore.TagExistsAsync` | `false` |
| `IEventStore.GetLatestTagAsync` | 空の `TagState`（version 0） |
| `IEventStore.GetLatestSortableUniqueIdAsync` | `""` |
| `IMultiProjectionStateStore.GetLatestForVersionAsync` / `GetLatestAnyVersionAsync` | `null`（10.x は空の `OptionalValue`） |
| `IMultiProjectionStateStore.DeleteAsync` | 何も削除しなかったら `false` |
| `IMultiProjectionStateStore.DeleteAllAsync` | 削除した件数（`0`） |
| `IGenerationAwareCheckpointStore.ReadCheckpointSlotAsync` | `CheckpointSlot.Absent` |
| `IColdObjectStorage.GetAsync` | `null`（各プロバイダーが以前 not-found を返していた場面と同じ） |
| `IColdObjectStorage.DeleteAsync` | オブジェクトがなかったら `false` |
| `IColdObjectStorage.OpenReadAsync` | 送出。既定の実装は `KeyNotFoundException` |

compare-and-set の拒否は値のままです。`IProjectionStatusStore.UpsertAsync` の `ProjectionStatusWriteResult` と、
チェックポイントの CAS メンバーの `CheckpointCasOutcome` です（どちらも 10.x から値でした）。

### `IEventStore`（Sekiban.Dcb.Storage）

```csharp
// 10.x
Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag);
Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag);
Task<ResultBox<bool>> TagExistsAsync(ITag tag);
Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null);
Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null);
Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null);
Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount);
Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId);
Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null);
Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
    IEnumerable<SerializableEvent> events);
Task<ResultBox<string>> GetLatestSortableUniqueIdAsync();

// 11
Task<IEnumerable<TagStream>> ReadTagsAsync(ITag tag);
Task<TagState> GetLatestTagAsync(ITag tag);
Task<bool> TagExistsAsync(ITag tag);
Task<long> GetEventCountAsync(SortableUniqueId? since = null);
Task<IEnumerable<TagInfo>> GetAllTagsAsync(string? tagGroup = null);
Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null);
Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount);
Task<SerializableEvent?> ReadSerializableEventAsync(Guid eventId);
Task<IEnumerable<SerializableEvent>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null);
Task<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)> WriteSerializableEventsAsync(
    IEnumerable<SerializableEvent> events);
Task<string> GetLatestSortableUniqueIdAsync();
```

`EventStoreExtensions` のヘルパーも同じです。`ReadAllEventsAsync` と `ReadEventsByTagAsync` は
`Task<IEnumerable<Event>>`、`ReadEventAsync` は `Task<Event?>`（イベントがなければ `null`）、`WriteEventsAsync` は
`Task<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>`、`WriteEventAsync` は `Task<Event>`
を返します。`EventSerializationExtensions.ToEvent` は `Event` を返し、失敗すると送出します。

### `IMultiProjectionStateStore`（Sekiban.Dcb.Storage）

```csharp
// 10.x
Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
    string projectorName, string projectorVersion, CancellationToken cancellationToken = default);
Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(
    string projectorName, CancellationToken cancellationToken = default);
Task<ResultBox<bool>> UpsertAsync(MultiProjectionStateRecord record, int offloadThresholdBytes = 1_000_000,
    CancellationToken cancellationToken = default);
Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(CancellationToken cancellationToken = default);
Task<ResultBox<bool>> DeleteAsync(string projectorName, string projectorVersion,
    CancellationToken cancellationToken = default);
Task<ResultBox<int>> DeleteAllAsync(string? projectorName = null, CancellationToken cancellationToken = default);
Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(MultiProjectionStateRecord record,
    CancellationToken cancellationToken = default);
Task<ResultBox<bool>> UpsertFromStreamAsync(MultiProjectionStateWriteRequest request, Stream stream,
    int offloadThresholdBytes, CancellationToken cancellationToken = default);

// 11
Task<MultiProjectionStateRecord?> GetLatestForVersionAsync(
    string projectorName, string projectorVersion, CancellationToken cancellationToken = default);
Task<MultiProjectionStateRecord?> GetLatestAnyVersionAsync(
    string projectorName, CancellationToken cancellationToken = default);
Task<bool> UpsertAsync(MultiProjectionStateRecord record, int offloadThresholdBytes = 1_000_000,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<ProjectorStateInfo>> ListAllAsync(CancellationToken cancellationToken = default);
Task<bool> DeleteAsync(string projectorName, string projectorVersion,
    CancellationToken cancellationToken = default);
Task<int> DeleteAllAsync(string? projectorName = null, CancellationToken cancellationToken = default);
Task<Stream> OpenStateDataReadStreamAsync(MultiProjectionStateRecord record,
    CancellationToken cancellationToken = default);
Task<bool> UpsertFromStreamAsync(MultiProjectionStateWriteRequest request, Stream stream,
    int offloadThresholdBytes, CancellationToken cancellationToken = default);
```

`OpenStateDataReadStreamAsync` と `UpsertFromStreamAsync` の既定の実装は、`NotSupportedException` で失敗した
タスクを返します（10.x は同じ例外を持つ失敗の箱）。ほかの失敗は、以前箱に入っていた型の例外を送出します。

### ステータスとチェックポイントのストア

```csharp
// IProjectionStatusStore (Sekiban.Dcb.Storage)
// 10.x
Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(ProjectionStatusHeartbeat heartbeat, long expectedSequence,
    CancellationToken cancellationToken = default);
Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAsync(string? projectorName = null,
    string? projectorVersion = null, CancellationToken cancellationToken = default);
// 11 (the convenience UpsertAsync overload and ListAllAsync change the same way)
Task<ProjectionStatusWriteResult> UpsertAsync(ProjectionStatusHeartbeat heartbeat, long expectedSequence,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<ProjectionStatusHeartbeat>> ListAsync(string? projectorName = null,
    string? projectorVersion = null, CancellationToken cancellationToken = default);

// IGenerationAwareCheckpointStore (Sekiban.Dcb.Storage.Checkpoints)
// 10.x
Task<ResultBox<CheckpointSlot>> ReadCheckpointSlotAsync(string projectorName, string projectorVersion,
    CancellationToken cancellationToken = default);
// 11 (CheckpointSlot.Absent when no row exists; the CAS members are unchanged)
Task<CheckpointSlot> ReadCheckpointSlotAsync(string projectorName, string projectorVersion,
    CancellationToken cancellationToken = default);
```

便利オーバーロードの `UpsertAsync` と `ListAllAsync` も同じように変わりました。行がなければ
`CheckpointSlot.Absent` を返します。CAS のメンバーは変わっていません。

### 条件付きストアと expected-tag-position ストア

```csharp
// IConditionalEventStore (Sekiban.Dcb.Storage)
// 10.x
Task<ResultBox<ConditionalAppendReceipt>> AppendIfUniqueAsync(ConditionalAppendRequest request,
    CancellationToken cancellationToken = default);
// 11
Task<ConditionalAppendReceipt> AppendIfUniqueAsync(ConditionalAppendRequest request,
    CancellationToken cancellationToken = default);

// IExpectedTagPositionEventStore (Sekiban.Dcb.Storage)
// 10.x
Task<ResultBox<bool>> EnsureExpectedTagPositionEnforcementEnabledAsync(CancellationToken cancellationToken = default);
Task<ResultBox<ExpectedTagPositionWriteResult>> WriteSerializableEventsWithExpectedTagPositionsAsync(
    IReadOnlyList<SerializableEvent> events, ExpectedTagPositionSpecification specification,
    CancellationToken cancellationToken = default);
// 11
Task<bool> EnsureExpectedTagPositionEnforcementEnabledAsync(CancellationToken cancellationToken = default);
Task<ExpectedTagPositionWriteResult> WriteSerializableEventsWithExpectedTagPositionsAsync(
    IReadOnlyList<SerializableEvent> events, ExpectedTagPositionSpecification specification,
    CancellationToken cancellationToken = default);
```

`Appended` と `AlreadyCommittedSameOperation` はレシートとして返ります。同じキーで別の操作をすると
`KeyReuseConflictException`、結果を確定できなければ `ConditionalAppendInDoubtException`、expected position の衝突や
検証エラーは `ExpectedTagPositionConflictException` と `TagHeadExpectationValidationException` が送出されます。
いずれも 10.x で失敗の箱に入っていた例外型です。`ConditionalAppendCoordinator` と `ConditionalAppendExecution` も
同じ形になりました。

### ストリーミング

```csharp
// IStreamingSerializableEventStore (Sekiban.Dcb.Storage)
// 10.x
Task<ResultBox<SerializableEventStreamReadResult>> StreamAllSerializableEventsAsync(SortableUniqueId? since,
    int? maxCount, Func<SerializableEvent, ValueTask> onEvent, CancellationToken cancellationToken = default);
// 11
Task<SerializableEventStreamReadResult> StreamAllSerializableEventsAsync(SortableUniqueId? since,
    int? maxCount, Func<SerializableEvent, ValueTask> onEvent, CancellationToken cancellationToken = default);

// IStreamingTaggedSerializableEventStore (Sekiban.Dcb.Storage)
// 10.x
Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(ITag tag,
    SortableUniqueId? since, SortableUniqueId? until, Func<SerializableEvent, ValueTask> onEvent,
    CancellationToken cancellationToken = default);
// 11
Task<SerializableEventStreamReadResult> StreamSerializableEventsByTagAsync(ITag tag,
    SortableUniqueId? since, SortableUniqueId? until, Func<SerializableEvent, ValueTask> onEvent,
    CancellationToken cancellationToken = default);
```

キャンセル以外の失敗は送出し、キャンセルはそのまま伝えてください。呼び出し側の `onEvent` コールバックが送出した
例外は、手を加えずに伝える必要があります。

### コールドストレージ（Sekiban.Dcb.ColdEvents）

```csharp
// IColdObjectStorage
// 10.x
Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct);
Task<ResultBox<Stream>> OpenReadAsync(string path, CancellationToken ct);
Task<ResultBox<bool>> PutAsync(string path, Stream data, string? expectedETag, CancellationToken ct);
Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct);
Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct);
Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct);
// 11
Task<ColdStorageObject?> GetAsync(string path, CancellationToken ct);
Task<Stream> OpenReadAsync(string path, CancellationToken ct);
Task<bool> PutAsync(string path, Stream data, string? expectedETag, CancellationToken ct);
Task<bool> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct);
Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct);
Task<bool> DeleteAsync(string path, CancellationToken ct);
```

ほかのコールド系の契約も同じように形が変わりました。

| インターフェイス | 11 のメンバー |
|---|---|
| `IColdSegmentFormatHandler` | `Task<ColdSegmentStreamResult> StreamSegmentAsync(Stream data, SortableUniqueId? since, int? maxCount, Func<SerializableEvent, ValueTask> onEvent, CancellationToken ct)` |
| `IColdLeaseManager` | `Task<ColdLease> AcquireAsync(...)`、`Task<ColdLease> RenewAsync(...)`、`Task<bool> ReleaseAsync(...)` |
| `IColdEventProgressReader` | `Task<ColdStoreProgress> GetProgressAsync(string serviceId, CancellationToken ct)` |
| `IColdEventCatalogReader` | `Task<ColdDataRangeSummary> GetDataRangeSummaryAsync(string serviceId, CancellationToken ct)` |
| `IColdEventExporter` | `Task<ExportResult> ExportIncrementalAsync(string serviceId, CancellationToken ct)` |

リースを取得・更新・解放できない場合は送出します（組み込みの `StorageBackedColdLeaseManager` は
`InvalidOperationException`）。10.x で失敗の箱になっていた場面と同じです。

### 永続サブスクリプションのストア（Sekiban.Dcb.Subscriptions）

`IDurableSubscriptionStore` の 10 個のメンバーはすべて箱が外れました。引数は変わりません。

```csharp
// 10.x → 11
Task<ResultBox<DurableSubscriptionState>> InitializeOrGetAsync(...)  → Task<DurableSubscriptionState> InitializeOrGetAsync(...)
Task<ResultBox<DurableSubscriptionState>> ReadAsync(...)             → Task<DurableSubscriptionState> ReadAsync(...)
Task<ResultBox<DurableSubscriptionLease>> TryAcquireAsync(...)       → Task<DurableSubscriptionLease> TryAcquireAsync(...)
Task<ResultBox<bool>> RenewAsync(...)                                → Task<bool> RenewAsync(...)
Task<ResultBox<bool>> IsRetryDueAsync(...)                           → Task<bool> IsRetryDueAsync(...)
Task<ResultBox<DurableSubscriptionState>> AcknowledgeAsync(...)      → Task<DurableSubscriptionState> AcknowledgeAsync(...)
Task<ResultBox<DurableSubscriptionState>> RecordFailureAsync(...)    → Task<DurableSubscriptionState> RecordFailureAsync(...)
Task<ResultBox<DurableSubscriptionState>> ResumeAsync(...)           → Task<DurableSubscriptionState> ResumeAsync(...)
Task<ResultBox<DurableSubscriptionState>> HaltAsync(...)             → Task<DurableSubscriptionState> HaltAsync(...)
Task<ResultBox<DurableSubscriptionState>> MarkIdleAsync(...)         → Task<DurableSubscriptionState> MarkIdleAsync(...)
```

`DurableSubscriptionRunner` は、10.x と同じ箇所でストアの失敗をログに出して処理します。

### 組み込みプロバイダーのクラス

組み込みストアの公開メンバーも契約に合わせて変わりました。`InMemoryEventStore`、
`InMemoryMultiProjectionStateStore`、`PostgresEventStore`、`PostgresMultiProjectionStateStore`、
`PostgresDurableSubscriptionStore`、`CosmosDbEventStore`、`CosmosMultiProjectionStateStore`、`DynamoDbEventStore`、
`DynamoMultiProjectionStateStore`、`SqliteEventStore`、`SqliteMultiProjectionStateStore`、コールドオブジェクト
ストレージ（JSONL、SQLite、DuckDB、Azure Blob database、`AzureBlobColdObjectStorage`、`S3ColdObjectStorage`）、
Core.Testing のストア（`InMemoryConditionalEventStore`、`DelegatingCheckpointStore`、
`DelegatingMultiProjectionStateStore`）です。SQLite の `TagEventService`・`TagListService`・`TagStateService` は
Core のサービスに処理を委ね、値を返します。

## 独自の `I*Types` とコア契約の実装

Core.Model のレジストリとディスパッチャー（特に記載がなければ名前空間は `Sekiban.Dcb.Domains`）は値を返します。
登録されていない名前は構成のエラーとして扱い、以前失敗の箱に入っていたのと同じ型・メッセージの例外を送出します。
想定内の検索結果は値のままです。

```csharp
// ICoreMultiProjectorTypes (10.x returned ResultBox<...> for each of these)
IMultiProjectionPayload Project(string multiProjectorName, IMultiProjectionPayload payload, Event ev,
    List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold);
string GetProjectorVersion(string multiProjectorName);
Func<IMultiProjectionPayload> GetInitialPayloadGenerator(string multiProjectorName);
Type GetProjectorType(string multiProjectorName);
IMultiProjectionPayload GenerateInitialPayload(string multiProjectorName);
IMultiProjectionPayload Deserialize(byte[] data, string multiProjectorName, JsonSerializerOptions jsonOptions);
SerializationResult Serialize(string projectorName, DcbDomainTypes domainTypes, string safeWindowThreshold,
    IMultiProjectionPayload payload);
SerializationSizeInfo SerializeToStream(string projectorName, DcbDomainTypes domainTypes, string safeWindowThreshold,
    IMultiProjectionPayload payload, Stream destination);
IMultiProjectionPayload Deserialize(string projectorName, DcbDomainTypes domainTypes, string safeWindowThreshold,
    byte[] data);
IMultiProjectionPayload DeserializeJson(string projectorName, string json, DcbDomainTypes domainTypes);
bool RegisterProjectorWithCustomSerialization<T>()
    where T : ICoreMultiProjectorWithCustomSerialization<T>, new();

// IStreamingMultiProjectorTypes
Task<IMultiProjectionPayload> DeserializeFromStreamAsync(string projectorName, DcbDomainTypes domainTypes,
    string safeWindowThreshold, Stream source, CancellationToken cancellationToken = default);

// ITagProjectorTypes
Func<ITagStatePayload, Event, ITagStatePayload> GetProjectorFunction(string tagProjectorName);
string GetProjectorVersion(string tagProjectorName);
string? TryGetProjectorForTagGroup(string tagGroupName);   // unchanged: null when none

// ITagStatePayloadTypes
string GetPayloadName(ITagStatePayload payload);
Type? GetPayloadType(string payloadName);                  // unchanged: null when unknown
ITagStatePayload DeserializePayload(string payloadName, byte[] jsonBytes);
byte[] SerializePayload(ITagStatePayload payload);

// ICoreQueryTypes (Sekiban.Dcb.Queries); projectorProvider was Func<Task<ResultBox<IMultiProjectionPayload>>>
Task<object> ExecuteQueryAsync(IQueryCommon query, Func<Task<IMultiProjectionPayload>> projectorProvider,
    IServiceProvider serviceProvider, int? safeVersion = null, string? safeWindowThreshold = null,
    DateTime? safeWindowThresholdTime = null, int? unsafeVersion = null);
Task<object> ExecuteListQueryAsync(IListQueryCommon query, Func<Task<IMultiProjectionPayload>> projectorProvider,
    IServiceProvider serviceProvider, int? safeVersion = null, string? safeWindowThreshold = null,
    DateTime? safeWindowThresholdTime = null, int? unsafeVersion = null);
Task<ListQueryResultGeneral> ExecuteListQueryAsGeneralAsync(IListQueryCommon query,
    Func<Task<IMultiProjectionPayload>> projectorProvider, IServiceProvider serviceProvider, int? safeVersion = null,
    string? safeWindowThreshold = null, DateTime? safeWindowThresholdTime = null, int? unsafeVersion = null);
Type GetMultiProjectorType(IQueryCommon query);
Type GetMultiProjectorType(IListQueryCommon query);
```

`SerializeToStream` と `DeserializeJson` の既定の実装は、失敗の箱を返す代わりに送出します。組み込みの
`SimpleTagProjectorTypes`・`SimpleTagStatePayloadTypes`・`SimpleQueryTypes`・`Aot*Types` クラス・
`ProjectionHeadStatusUtilities` も同じ形です。独自の `IQueryTypes` は `null` を返してはいけません。Orleans の
クエリ実行は `InvalidOperationException("Query X completed without a result.")` で失敗します。

静的なコア契約も値を返します。

```csharp
// ICoreMultiProjector<T> (Sekiban.Dcb.MultiProjections); was static abstract ResultBox<T> Project(...)
static abstract T Project(T payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes,
    SortableUniqueId safeWindowThreshold);

// ICoreMultiProjectionQuery<TMultiProjector, TQuery, TOutput> (Sekiban.Dcb.Queries)
static abstract TOutput HandleQuery(TMultiProjector projector, TQuery query, IQueryContext context);

// ICoreMultiProjectionListQuery<TMultiProjector, TQuery, TOutput> (Sekiban.Dcb.Queries)
static abstract IEnumerable<TOutput> HandleFilter(TMultiProjector projector, TQuery query, IQueryContext context);
static abstract IEnumerable<TOutput> HandleSort(IEnumerable<TOutput> filteredList, TQuery query, IQueryContext context);

// ICoreCommandContext (Sekiban.Dcb.Commands); each member was Task<ResultBox<...>>
Task<TagStateTyped<TState>> GetStateAsync<TState, TProjector>(ITag tag) where TState : ITagStatePayload
    where TProjector : ITagProjector<TProjector>;
Task<TagState> GetStateAsync<TProjector>(ITag tag) where TProjector : ITagProjector<TProjector>;
Task<bool> TagExistsAsync(ITag tag);
Task<string> GetTagLatestSortableUniqueIdAsync(ITag tag);   // "" when the tag has no events
Task<EventOrNone> AppendEvent(IEventPayload ev, params ITag[] tags);
Task<EventOrNone> AppendEvent(EventPayloadWithTags eventPayloadWithTags);
```

イベントのないタグに対する `GetStateAsync` は空の状態を返します。

## コアランタイムの契約

| 契約（名前空間） | 11 の形 |
|---|---|
| `IActorObjectAccessor`（`Sekiban.Dcb.Actors`） | `Task<T> GetActorAsync<T>(string actorId) where T : class;` 失敗はタスクの失敗になる |
| `ITagConsistentActorCommon`（`Sekiban.Dcb.Actors`） | `Task<string> GetLatestSortableUniqueIdAsync();` `Task<TagWriteReservation> MakeReservationAsync(string? lastSortableUniqueId);` 衝突は同じ例外・メッセージで送出 |
| `ISerializedSekibanDcbExecutor` | `Task<SerializableTagState> GetSerializableTagStateAsync(TagStateId tagStateId);` `Task<SerializedCommitResult> CommitSerializableEventsAsync(SerializedCommitRequest request, CancellationToken cancellationToken = default);` |
| `ISerializedExpectedTagPositionSekibanDcbExecutor` | `Task<SerializedCommitResult> CommitSerializableEventsWithExpectedTagPositionsAsync(...)` |
| `ISerializedConditionalSekibanDcbExecutor` | `Task<SerializedConditionalCommitResult> CommitSerializableEventConditionallyAsync(...)` |
| `ISerializedCommitAcceptor`（`Sekiban.Dcb.Commands`） | `Task<SerializedCommitResult> AcceptAsync(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken = default);` |
| `IProjectionStatusReader` | `Task<IReadOnlyList<ProjectionStatusSnapshot>> ReadAsync(...)` と `GetSnapshotsAsync(...)` |
| `ISerializedProjectionStatusReader` | `AcceptAsync`・`ReadSerializedAsync`・`ReadSerializedV2Async`・`GetSerializedSnapshotsAsync` は `Task<byte[]>` |
| `IMultiProjectionProjectionAccumulator`（`Sekiban.Dcb.Runtime`） | `SerializableMultiProjectionStateEnvelope GetSnapshot();` `MultiProjectionStateMetadata GetMetadata();` |

`CoreGeneralSekibanExecutor.ExecuteAsync` のハンドラーは `Func<TCommand, ICoreCommandContext, Task<EventOrNone>>`
になりました（以前は `Task<ResultBox<EventOrNone>>`）。クエリ・タグ状態・ヘッド状態のメンバーも値を返します。
`ProjectionStatusReadWindowCache` は `Func<Task<long>>` のサンプラーを受け取ります。予約の失敗は、これまでどおり
"Failed to reserve tags" にまとめられます。

シリアライズ済みのステータスリーダーを公開しているエンドポイントは、バイト列をそのまま書き出します。

```csharp
app.MapGet("/ops/projection-status", async (ISerializedProjectionStatusReader reader) =>
        Results.Bytes(await reader.ReadSerializedAsync(), "application/json"))
   .RequireAuthorization("ProjectionStatusOperator");
```

## クエリ

- **`IQueryCommon` だけを実装し、`ResultBox` を返す静的な `HandleQuery` を持つクエリはサポートされなくなりました。**
  箱を返すハンドラーなら WithResult の `IMultiProjectionQuery` / `IMultiProjectionListQuery` を、値を返すハンドラー
  ならコアの `ICoreMultiProjectionQuery` / `ICoreMultiProjectionListQuery` を実装してください。該当するクエリは、
  実行するたびに `InvalidOperationException` で失敗します。メッセージは型名を含み、"A query whose handlers return a
  ResultBox must implement the WithResult IMultiProjectionQuery interface; otherwise implement
  ICoreMultiProjectionQuery and return the value directly." で終わります（リストクエリではリスト用のインターフェイス
  名になります）。`TypeInitializationException` にはならず、ほかのクエリ型には影響しません。
- WithoutResult のクエリが通る名前ベースの経路では、ハンドラーが実行時に `object` として返した `ResultBox` を Core が
  開かなくなりました。箱そのものが結果の値になります。影響するのは、出力型が `object` で箱を返す WithoutResult の
  クエリだけです。
- **値型を出力するリストクエリ**（`int` など）が、コア契約経由で動くようになりました。10.x では `HandleSort` で
  失敗していました。
- クエリは、キャッシュしたデリゲートを使い、例外を包まずにコア契約経由で実行されます。

## Orleans

### クラスターの全面再起動

grain インターフェイスの形が変わり、`ITagConsistentGrain` にはメンバーが追加されました。**10.x と 11 の silo や
クライアントを同じクラスターで動かすことはできません。10.x のクラスターをすべて止めてから 11 を起動してください。**
永続化された grain の状態（`MultiProjectionGrainState`、`TagStateCacheState`）、スナップショットのエンベロープ、
イベントは変わっていません。削除された `ResultBoxSurrogate` は RPC でだけ使われ、永続化されたことはありません。

### grain インターフェイス（Sekiban.Dcb.Orleans.Grains）

```csharp
// IMultiProjectionGrain
// 10.x
Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true, bool waitForCatchUp = false);
Task<ResultBox<string>> GetSnapshotJsonAsync(bool canGetUnsafeState = true);
Task<ResultBox<bool>> PersistStateAsync();
Task<ResultBox<ProjectionFaultReadResult>> TryGetProjectionFaultAsync();
Task<ResultBox<bool>> ResetProjectionFaultAsync(ResetProjectionFaultRequest request);
// 11
Task<MultiProjectionState> GetStateAsync(bool canGetUnsafeState = true, bool waitForCatchUp = false);
Task<string> GetSnapshotJsonAsync(bool canGetUnsafeState = true);
Task<bool> PersistStateAsync();
Task<ProjectionFaultReadResult> TryGetProjectionFaultAsync();
Task<bool> ResetProjectionFaultAsync(ResetProjectionFaultRequest request);

// ITagConsistentGrain
// 10.x
Task<ResultBox<string>> GetLatestSortableUniqueIdAsync();
Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string? lastSortableUniqueId);
// 11
Task<string> GetLatestSortableUniqueIdAsync();
Task<string?> TryGetLatestSortableUniqueIdAsync();   // new
Task<TagWriteReservation> MakeReservationAsync(string? lastSortableUniqueId);
```

- `PersistStateAsync` は、永続化の結果を残さずに止まった場合（整合性ガード、保留中のリビルド）に `false` を返します。
  試行そのものが失敗した場合はタスクが失敗します。
- `TryGetProjectionFaultAsync` の既定の実装は、`NotSupportedException` で失敗したタスクを返します。
- `ResetProjectionFaultAsync` は、トークンが一致しない場合に同じ `InvalidOperationException` でタスクを失敗させます。
- `TryGetLatestSortableUniqueIdAsync` は、アクターが失敗を報告した場合（最新位置が不明）に `null` を返し、grain
  呼び出し自体が失敗した場合にだけタスクを失敗させます。タグ状態の読み取りはこれを使い、アクター自身の失敗と通信・
  タイムアウトの失敗を 10.x と同じように区別しています。
- 公開メンバーでは、grain 呼び出しの失敗も grain が報告した失敗も、どちらもタスクの失敗になります。grain を直接
  呼んで `IsSuccess` を見ていたコードは、例外を捕まえる形に変えてください。

### 独自の `IProjectionActorHost`（Sekiban.Dcb.Runtime）

```csharp
// 11 (10.x returned Task<ResultBox<...>> for each of these)
Task<ProjectionStateMetadata> GetStateMetadataAsync(bool includeUnsafe = true);
Task<MultiProjectionState> GetStateAsync(bool canGetUnsafeState = true);
Task<bool> WriteSnapshotToStreamAsync(Stream target, bool canGetUnsafeState, CancellationToken cancellationToken);
Task<bool> WriteSnapshotForPersistenceToStreamAsync(Stream target, bool canGetUnsafeState,
    int offloadThresholdBytes, CancellationToken cancellationToken);
Task<bool> RestoreSnapshotFromStreamAsync(Stream source, CancellationToken cancellationToken);
Task<SerializableQueryResult> ExecuteQueryAsync(SerializableQueryParameter query, int? safeVersion,
    string? safeThreshold, DateTime? safeThresholdTime, int? unsafeVersion);
Task<SerializableListQueryResult> ExecuteListQueryAsync(SerializableQueryParameter query, int? safeVersion,
    string? safeThreshold, DateTime? safeThresholdTime, int? unsafeVersion);
Task<bool> RewriteSnapshotVersionAsync(Stream source, Stream target, string newVersion,
    CancellationToken cancellationToken);
```

スナップショットを書き込むメンバーは、永続的なスナップショットを書かなかった場合に `false` を返します。書き込みや
復元に失敗したら送出してください。スナップショットの書き込み・復元で例外を送出したホストは、ホストが失敗を報告
した場合と同じ扱い（警告ログと、失敗した永続化の試行）になります。組み込みの実装は `NativeProjectionActorHost` です。

### シリアライズのヘルパー（Sekiban.Dcb.Orleans.Serialization）

`SerializableQueryParameter.ToQueryAsync` は `Task<object>` を返します。`SerializableQueryResult.CreateFromResultBoxAsync`
は `object result` を、`SerializableListQueryResult.CreateFromResultBoxAsync` は `IListQueryResult listQueryResult` を
受け取ります。`ToQueryResultAsync` は `Task<QueryResultGeneral>`、`ToListQueryResultAsync` は
`Task<ListQueryResultGeneral>`、`SerializableQueryResult.ToResultBoxAsync` は `Task<object>`（値そのもの）を返します。
`ResultBox` はもう使っていませんが、名前はそのままです。10.x で包まずに返していた失敗は、引き続き包まずに送出し、
それ以外は "Failed to deserialize …" の `InvalidOperationException` のままです。`OrleansActorObjectAccessor` は
`IActorObjectAccessor` に従います。

### 例外の転送

Orleans 10.4.2 がそのままシリアライズできる例外は、System・Microsoft・Azure の型です。コーデックのないそれ以外の
例外型は、呼び出し側に `CodecNotFoundException` として届き、メッセージが失われます。11 では次のように扱います。

- **Sekiban の例外のコンバーター**を Orleans.Core に追加しました。`[RegisterConverter]` で登録されるので設定は
  不要です。対象は `ExpectedTagPositionConflictException`、`ConditionalAppendInDoubtException`、
  `KeyReuseConflictException`、`TagHeadExpectationValidationException`（いずれも `Sekiban.Dcb.Storage`）と、以前から
  ある `SekibanProjectionFaultException`（`Sekiban.Dcb.Actors`、記述子を含む）です。コンストラクター経由で作り直す
  ので、型・メッセージ・プロパティが届きます。スタックトレースとそれ以外の `Data` は届きません
  （`SekibanProjectionFaultException` は届いた側で fault の注釈を付け直します）。
- **以前箱を返していたメンバー**（上の `IMultiProjectionGrain` の 5 つと `ITagConsistentGrain` の 2 つ）では、Orleans
  がシリアライズできる失敗は同じ例外インスタンスのまま届きます。送る前に、`InnerException`、
  `AggregateException.InnerExceptions`、`Data` だけをたどります。同じ例外インスタンスに 2 度出会った場合（たとえば
  同じ内部例外を 2 回含む `AggregateException`）、深さが 64 を超えた場合、1024 個を超えた場合は確認が失敗し、下の
  フォールバックになります。
  `Data` のキーと値はすべて、`null`・文字列・プリミティブ・列挙型・`decimal`・`DateTime`・`DateTimeOffset`・
  `TimeSpan`・`Guid` のいずれかである必要があります。ただし上の 5 つのコンバーター対象の型は例外です。サロゲートは
  自分のフィールドしか運ばないので `Data` は調べません（`Data` が自分自身を参照している
  `SekibanProjectionFaultException` もコンバーター経由で届きます）。内部例外を調べるのは、サロゲートが内部例外を運ぶ
  `ConditionalAppendInDoubtException` と `KeyReuseConflictException` だけです。この確認のあとで、silo のシリアライザー
  で実際にシリアライズできるかを試します。
- **フォールバック。** 条件を満たさない失敗は `Exception("[TypeName] message")` になります。10.x がこれらのメンバーで
  `SekibanProjectionFaultException` 以外のすべての失敗に使っていた形です。判定は保守的で、Sekiban のコンバーターがない例外のうち、自身または内部例外の `Data` に複雑な値を持つ
  ものは、Orleans が運べる場合でも平坦化されます。
- **キャンセルは保たれます。** 運べないキャンセルは、同じメッセージの `OperationCanceledException`（または
  `TaskCanceledException`）になり、内部例外と `Data` は落ちます。

まとめると、コンバーターのある Sekiban の型は型・メッセージ・プロパティを保ち、System の型は型とメッセージを保ち、
コーデックのない型は `[TypeName] message` の形のままです。**コーデックがあるところでは `[TypeName]` の接頭辞は
付かなくなりました。** これらの grain メンバーを呼ぶ側は、どちらのファサードを使っていても失敗したタスクを受け取り
ます。どちらの `OrleansDcbExecutor` も `IMultiProjectionGrain` のこれらのメンバーは公開していません。エグゼキューターの
経路では、`ICommandContext.TagExistsAsync` と `GetTagLatestSortableUniqueIdAsync` が
`ITagConsistentGrain.GetLatestSortableUniqueIdAsync` に届き（転送された例外は WithResult では失敗の箱に入り、
WithoutResult では送出されます）、予約が `MakeReservationAsync` に届きます。予約の失敗は
`InvalidOperationException("Failed to reserve tags: ...")` にまとめられます。

## MaterializedView

`MvExecutorBase<T>` の protected な `ResultBox` のオーバーロードは置き換えられました。

```csharp
// 10.x (removed)
protected Task<MvCatchUpResult> CompleteCatchUpAsync(IMvApplyHost host, string serviceId,
    ResultBox<IEnumerable<SerializableEvent>> readResult, MvProjectionStatusSnapshot currentStatus,
    CancellationToken cancellationToken)
protected async Task<MvCatchUpResult> CompleteCatchUpAsync(IMvApplyHost host, string serviceId,
    ResultBox<IEnumerable<SerializableEvent>> readResult, MvProjectionStatusSnapshot currentStatus,
    CancellationToken cancellationToken, bool classifyFailures)

// 11: the events you read
protected Task<MvCatchUpResult> CompleteCatchUpAsync(IMvApplyHost host, string serviceId,
    IEnumerable<SerializableEvent> readResult, MvProjectionStatusSnapshot currentStatus,
    CancellationToken cancellationToken)
protected async Task<MvCatchUpResult> CompleteCatchUpAsync(IMvApplyHost host, string serviceId,
    IEnumerable<SerializableEvent> readResult, MvProjectionStatusSnapshot currentStatus,
    CancellationToken cancellationToken, bool classifyFailures)

// 11: why the read failed (new)
protected async Task<MvCatchUpResult> CompleteCatchUpAsync(IMvApplyHost host, string serviceId,
    Exception readFailure, MvProjectionStatusSnapshot currentStatus,
    CancellationToken cancellationToken, bool classifyFailures = true)
```

読み取りに成功したらイベントを、失敗したら例外を渡してください。例外は、`CatchUpOnceAsync` の中で読み取りに
失敗した場合とまったく同じように（`HandleCatchUpReadFailure` を通して）分類されます。

## 形以外の動作の変更

ここに挙げていないものは、形の変更を除いて 10.x と同じ動作です。

### ストレージとプロバイダー

1. 見つからない読み取りは、エラーではなく `null` を返します。存在するのに読めない SQLite の行は、10.x と同じく
   `KeyNotFoundException` を送出します。
2. コールド側の経路は、呼び出し側自身のトークンがキャンセルされない限り 10.x と同じ結果になります。
   `HybridEventStore` のリスト読み取りは、コールド側のどんな失敗でもホット側に切り替えます。ストリーミング読み取りは、
   呼び出し側のトークンがキャンセルされていなければホット側に切り替えます。`ColdControlFileHelper` は、読めない
   マニフェストやチェックポイントを、呼び出し側のトークンがキャンセルされていなければ存在しないものとして扱います。
   `ColdExportCycleRunner` は、サイクルのトークンがキャンセルされていなければログを出してサイクルを終えます。
3. ランタイムと Orleans の呼び出し箇所は、ストレージの失敗が送出されたのか返されたのかを区別しなくなりました。grain
   自身のストリーミングコールバックが送出した例外は、これまでどおり伝わります。ストリーム読み取りの失敗として扱うのは
   ストアの失敗だけです。Cosmos DB では、10.x で外に漏れていた失敗（たとえば保存前のスナップショット読み取りでの 429）
   が、PostgreSQL・DynamoDB・SQLite がすでに通っていたスキップの経路を通るようになりました。grain の状態は保存され、
   外部スナップショットは次の永続化で再試行され、`_lastError` が設定されます。データが上書きされたり失われたりする
   ことはありません。
4. MaterializedView のターゲットチェックポイントの取得は、プロバイダーのタイムアウトを含め、ヘッドの読み取り失敗を
   すべて `ReadUnavailable` にします（10.x と同じ）。
5. コールドエクスポーターの末尾マージで、`MergeCapacityExceededException` が本来の catch に届くようになりました
   （計画は同じで、警告ログが 1 行減ります）。
6. ログ: DynamoDB は、10.x で箱として返していた失敗について新しいエラーログを出しません。SQLite のキャッシュ同期は、
   同期中のストアの失敗で "Cache sync failed" を出します（10.x で箱として返していたリモートの失敗でも出すように
   なりました）。

### Core.Model

7. レジストリに登録されていない名前は送出になります（例外の型とメッセージは、以前の失敗の箱と同じです）。
8. 古い Sekiban に対してコンパイルされたドメインのレジストリ（リポジトリ内の 10.18 のフィクスチャ）は、もう読み込め
   ません（`TypeLoadException`）。11 に対して再ビルドしてください。

### Core ランタイム

9. `ColdExporter`: ステージ済みのローカルセグメントの再読み取りに失敗すると、元の例外が伝わります（10.x は
   `GetValue()` からの `ResultsInvalidOperationException("no value")`）。
10. WithResult のコマンドハンドラーが null の `ResultBox` を返すと、`InvalidOperationException` で失敗します（以前は
    `NullReferenceException`）。クエリハンドラーのメッセージ（"HandleQuery returned null result"、"Query handler
    returned invalid result type"）は 10.x と同じです。
11. 同期的な送出は 10.x と同じ経路をたどります。10.x で箱にしていた場所（WithResult ファサード、`ColdExporter`、
    `DurableSubscriptionRunner`、コマンドコンテキストとタグ状態のヘルパー）で箱にするのは、失敗したタスクだけです。
12. クエリ: [クエリ](#クエリ)を参照してください（`IQueryCommon` だけで箱を返すハンドラーの非サポート、名前ベースの
    経路の `object` のケース、値型のリスト出力）。
13. WithoutResult と Orleans.WithoutResult のシリアライズ済みメンバーは値を返します（[WithoutResult を使っている場合](#withoutresult-を使っている場合)）。
14. `GeneralTagStateActor`（インメモリの経路のみ）: 最新位置の読み取りでの通信の失敗は「最新位置が不明」になります。
    10.x で失敗の箱がそう扱われていたのと同じです。
15. Orleans の `TagStateGrain` は Try メンバーで最新位置を読みます。アクターが報告した失敗は「最新位置が不明」、grain
    呼び出しの失敗（通信、タイムアウト）は 10.x と同じく伝わります。

### Orleans

16. 以前箱を返していた grain メンバーは、本来の例外型とメッセージを保ちます。コーデックがあるところでは
    `[TypeName]` の接頭辞が付きません。
17. `ITagConsistentGrain.TryGetLatestSortableUniqueIdAsync` を追加しました。
18. 公開メンバーでは、grain 呼び出しの失敗も grain が報告した失敗も、どちらもタスクの失敗になります。
19. `NativeProjectionQueryExecutor`: 独自の `IQueryTypes` が `null` を返すと、ResultBoxes の "no exception" エラーでは
    なく `InvalidOperationException("Query X completed without a result.")` で失敗します。
20. サードパーティの `IProjectionActorHost` がスナップショットの書き込み・復元で送出した場合、外側の
    "Persistence failed" ログではなく、ホストが失敗を報告した場合と同じ扱い（警告と失敗した試行）になります。
21. サンプルとテンプレートの persist・snapshot エンドポイント: grain 呼び出しの失敗（たとえば Orleans のタイムアウト）
    も、grain が報告した失敗と同じ警告と BadRequest の経路を通るようになりました。10.x では通信の失敗が例外
    ミドルウェアまで届いていました。どちらも同じ失敗したタスクとして届くので、例外型で確実に見分けることはできません。
22. ResultBox の null 値の癖（null の値を失敗とみなすもの。たとえば null のストリームや、デシリアライズ結果が null の
    ペイロード）はもう再現しません。そこで `null` を返すのは契約違反です。

### WithoutResult

23. `RegisterProjectorWithCustomSerialization` は、失敗すると元の例外を送出します（10.x は失敗の箱を返し、たいていは
    無視されていました）。
24. カスタムシリアライズのプロジェクター以外のアセンブリスキャン登録（`RegisterProjector`、イベント、タグ
    プロジェクター、タグ状態のペイロード、タグ型、クエリ、リストクエリ）は、両方のファサードで
    `TargetInvocationException` ではなく元の例外を表に出します。スキャンしたカスタムシリアライズのプロジェクターの
    登録が失敗した場合は、10.x と同じく黙ってスキップし、スキャンを続けます。
25. 汎用タグプロジェクターの公開静的 `Project` に `null` のペイロードを渡し、イベントにそのプロジェクターのタググループの
    タグがない場合、`InvalidOperationException("{Projector}.Project produced no payload: the payload passed in was null
    and the event carried no tag of the projector's tag group.")` を同じ境界情報付きで送出します（10.x は ResultBoxes の
    `ResultValueNullException`）。
26. 汎用タグプロジェクターのスナップショット復元は、ペイロードのデシリアライズで例外が出た項目をスキップします。10.x で
    スキップになっていたのは `SimpleTagStatePayloadTypes` だけで、`AotTagStatePayloadTypes` と利用者の
    `ITagStatePayloadTypes` では失敗がそのまま伝わっていました。
27. 箱専用だった null 結果のガードはなくなりました。null の `Task` や null の値は、これまでどおり
    `SekibanBoundaryException` になります。
28. WithoutResult の推移的な参照だけで ResultBox の型を使っていたアプリは、`ResultBoxes` を自分で参照する必要が
    あります。

### リポジトリ内部

内部サンプルの `/debug/events` エンドポイントは、ストアの失敗で 400 ではなく 500 を返します。テスト用ヘルパーの
`SeamWriteScanner` は IL を命令単位でデコードします。どちらもパッケージには含まれません。

## API の破壊的変更の記録

10.25.0 に対する意図的な破壊的変更は、メンバーと対象フレームワークごとに、`dcb/src/` の各パッケージの
`CompatibilitySuppressions.xml` に記録されています。そこにない破壊的変更があると、パッケージ検証が pack を失敗させます。
記録があるパッケージ: Core、Core.Model、Core.Testing、Postgres、CosmosDb、DynamoDB、Sqlite、ColdStorage、
BlobStorage.AzureStorage、BlobStorage.S3、MaterializedView、Orleans.Core、Orleans.WithoutResult、WithoutResult、
WithoutResult.Model、WithResult、WithResult.Model。記録がないパッケージ: Orleans.WithResult、WithResult.Testing、
WithoutResult.Testing、Orleans.AzureQueue、MaterializedView.Orleans、4 つの MaterializedView データベースプロバイダー。
