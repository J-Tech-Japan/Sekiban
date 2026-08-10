# コマンド・イベント・タグ・プロジェクター - Sekiban DCB

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [コマンド・イベント・タグ・プロジェクター](03_aggregate_command_events.md) (現在位置)
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
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md)

DCB では集約という概念をタグに置き換えます。コマンドは相変わらずユーザーの意図を表しますが、結果として
生成されるイベントは複数タグに紐づき、予約対象もタグ単位で管理されます。

## コマンド

コマンドはレコードで定義し、静的ハンドラーを通じて `ICommandContext` を受け取ります。

```csharp
public record CreateStudent : ICommandWithHandler<CreateStudent>
{
    [Required] public Guid StudentId { get; init; }
    [Required] public string Name { get; init; } = default!;
    [Range(1, 10)] public int MaxClassCount { get; init; } = 5;

    public static Task<ResultBox<EventOrNone>> HandleAsync(CreateStudent command, ICommandContext context) =>
        ResultBox.Start
            .Remap(_ => new StudentTag(command.StudentId))
            .Combine(context.TagExistsAsync)
            .Verify((_, exists) => exists
                ? ExceptionOrNone.FromException(new ApplicationException("Student Already Exists"))
                : ExceptionOrNone.None)
            .Conveyor((tag, _) => EventOrNone.EventWithTags(
                new StudentCreated(command.StudentId, command.Name, command.MaxClassCount),
                tag));
}
// internalUsages/Dcb.Domain/Student/CreateStudent.cs
```

`ICommandContext` はタグ状態取得 (`GetStateAsync`)、存在確認、イベント追加 (`AppendEvent`) などを提供します。

## イベントペイロード

イベントは `IEventPayload` を実装する不変レコードです。1つのコマンドが 1 つのイベントを返し、複数タグを対象
にできます。

```csharp
public record StudentEnrolledInClassRoom(Guid StudentId, Guid ClassRoomId) : IEventPayload;
// internalUsages/Dcb.Domain/Enrollment/StudentEnrolledInClassRoom.cs
```

## タグ

タグはイベントが影響する論理的主体を表します。`IGuidTagGroup<T>` などの補助インターフェースを利用すると
フォーマットを統一できます。

```csharp
public record StudentTag(Guid StudentId) : IGuidTagGroup<StudentTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "Student";
    public static StudentTag FromContent(string content) => new(Guid.Parse(content));
    public Guid GetId() => StudentId;
}
```

`IsConsistencyTag()` が true のタグのみが予約対象になります。集計用タグなど、整合性が不要なものは false を返し
ます。

### 3状態の予約セマンティクス (SEK-G19 / SEK-G30, 10.11.0)

整合性タグへの書き込みは、**期待バージョン**（タグの最後の `SortableUniqueId`）でタグを予約し、アクター内で
次の3状態として扱います。

- **null** は **Unspecified**（コマンドがタグを未観測）です。予約・確定・キャンセルのライフサイクルはすべて維持
  しますが、バージョン比較・再読込・キャッシュ採用だけを行いません。
- **空**の期待バージョンは「**このタグが空であることを期待する**」＝初回書き込みを意味します。タグにコミット済み
  状態が無い場合のみ成功し、既に状態を持つタグへの2つ目の（非オーバーラップな）初回書き込みは**衝突**します
  （既存の `ResultBox.Error` チャネルで表面化。新しい公開例外型は追加しません）。
- **非空**の期待バージョンはタグの現在バージョンと完全一致する必要があります。空タグへの非空期待や不一致は衝突し、
  完全一致は通常の更新です。

書き込みの期待バージョンの決め方:

- `GetStateAsync` で空状態を正常に観測した場合、または `TagExistsAsync` が `false` を返した場合は
  **AssertEmpty** で予約し、途中で別の初回書き込みが勝てば衝突します。
- 非空状態を観測した場合は、そのバージョンで完全一致予約します。
- 未観測の場合は **Unspecified** です。既存の副次整合性タグを読み取らず付与する更新が可能で、読込失敗を空観測へ
  変換することはありません。
- `ConsistencyTag.FromTagWithSortableUniqueId(...)` は明示的な期待バージョンをそのまま使用します。

**保証境界（クラスタ単位）**。Orleans ではこれは**クラスタあたり最大1回の初回書き込み**を保証します（タグごとに
1つのアクター活性化が予約を直列化）。独立したクラスタはアクターを介して協調しません。2つのクラスタがそれぞれ
expect-empty で予約し、同じタグに重複した作成を永続追加し得ます。クラスタ間の一意性は**ストレージ層**の役割で、
条件付きユニーク追加コントラクト（[ストレージプロバイダ](11_storage_providers.md)参照）が担い、永続化された重複の
収束はマルチプロジェクション層（SEK-G18）が扱います。クラスタ間で「このIDは既に存在する」を厳密に保証するには、
アクター予約ではなくストレージのユニーク追加に依存してください。

**10.11.0 リリースノート**: 未読の整合性タグは 10.1.x と同じ比較なし動作へ戻り、既存の副次タグを読み取らず付与
するコマンドを修復します。明示的に空を観測した初回書き込みは 10.8.0 の衝突保証を維持します。nullable metadata で
Unspecified と AssertEmpty を区別するため minor release ですが、CLR シグネチャは
`MakeReservationAsync(System.String)` のままです。

### 共有ストアでの stale-empty 再確認 (SEK-G22 / 10.8.2)

マルチクラスタ構成では、別クラスタの書き込みをコマンド側の fold が既に参照している一方で、アクターが正常に取得した
「空タグ」をキャッシュし続ける場合があります。fold がコミット済みの非空バージョンを渡すと、10.8.0 の完全一致検査は
stale な空キャッシュと比較し、正しい更新を誤って拒否していました。

10.8.2 以降、この異常形（期待バージョンが非空、アクターキャッシュが空）に限り、予約ロックを保持したまま権威タグ読み
取りを1回だけ行います。一致なら続行し、権威結果が空または別バージョンなら整合性衝突、読み取り失敗なら fail-closed
です。成功した読み取りは最終比較より前にキャッシュへ採用されるため、後続の expect-empty が既知の永続状態に対して通る
ことはありません。通常の一致・不一致と、期待バージョンが空のすべてのパスでは追加読み取りを行いません。

これは誤拒否の修正であり、クラスタ間一意性の追加ではありません。クラスタ間の重複防止は引き続きストレージの条件付き
ユニーク追加 (G15/G16) が担います。10.8.2 で API・スキーマ・既定値の変更や移行はありません。

## タグ状態ペイロード

タグ状態は `ITagStatePayload` を実装するレコードで表現し、プロジェクターがイベントを適用して更新します。

```csharp
[GenerateSerializer]
public record StudentState(Guid StudentId, string Name, int MaxClassCount, List<Guid> EnrolledClassRoomIds)
    : ITagStatePayload
{
    public int GetRemaining() => MaxClassCount - EnrolledClassRoomIds.Count;
}
```

## タグプロジェクター

`ITagProjector<T>` を実装し、静的メソッドでイベント適用ロジックを書くのが DCB 流です。

```csharp
public class StudentProjector : ITagProjector<StudentProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(StudentProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev) => (current, ev.Payload) switch
    {
        (EmptyTagStatePayload, StudentCreated created) => new StudentState(
            created.StudentId,
            created.Name,
            created.MaxClassCount,
            new List<Guid>()),

        (StudentState state, StudentEnrolledInClassRoom enrolled) when state.GetRemaining() > 0 => state with
        {
            EnrolledClassRoomIds = state.EnrolledClassRoomIds
                .Concat(new[] { enrolled.ClassRoomId })
                .Distinct()
                .ToList()
        },

        (StudentState state, StudentDroppedFromClassRoom dropped) => state with
        {
            EnrolledClassRoomIds = state.EnrolledClassRoomIds
                .Where(id => id != dropped.ClassRoomId)
                .ToList()
        },

        _ => current
    };
}
```

ProjectorVersion を変更するとアクターのキャッシュが破棄され、再計算が走ります。

## 複数タグを扱うコマンド

複数タグの状態を組み合わせてイベントを生成する場合は、`ResultBox` の `Remap` / `Combine` / `Verify` を使って
段階的に検証します。

```csharp
public class EnrollStudentInClassRoomHandler : ICommandHandler<EnrollStudentInClassRoom>
{
    public static Task<ResultBox<EventOrNone>> HandleAsync(EnrollStudentInClassRoom command, ICommandContext context) =>
        ResultBox.Start
            .Remap(_ => new StudentTag(command.StudentId))
            .Combine(context.GetStateAsync<StudentState, StudentProjector>)
            .Verify((_, studentState) => studentState.Payload.GetRemaining() <= 0
                ? ExceptionOrNone.FromException(new("Student has reached maximum class count"))
                : studentState.Payload.EnrolledClassRoomIds.Contains(command.ClassRoomId)
                    ? ExceptionOrNone.FromException(new("Student is already enrolled in this classroom"))
                    : ExceptionOrNone.None)
            .Remap((studentTag, _) => TwoValues.FromValues(studentTag, new ClassRoomTag(command.ClassRoomId)))
            .Combine((_, classRoomTag) => context.GetStateAsync<ClassRoomProjector>(classRoomTag))
            .Verify((_, _, classRoomState) => classRoomState.Payload switch
            {
                AvailableClassRoomState available when available.GetRemaining() <= 0 =>
                    ExceptionOrNone.FromException(new("ClassRoom is full")),
                AvailableClassRoomState available when available.EnrolledStudentIds.Contains(command.StudentId) =>
                    ExceptionOrNone.FromException(new("Student is already enrolled in this classroom")),
                FilledClassRoomState => ExceptionOrNone.FromException(new("ClassRoom is full")),
                _ => ExceptionOrNone.None
            })
            .Conveyor((studentTag, classRoomTag, _) => EventOrNone.EventWithTags(
                new StudentEnrolledInClassRoom(command.StudentId, command.ClassRoomId),
                studentTag,
                classRoomTag));
}
```

## 実行ユーザーの記録（SEK-G23）

すべてのイベントは `EventMetadata.ExecutedUser` を持ちます。既定ではコマンド経路でリテラル `"GeneralSekibanExecutor"`、シリアライズ/WASM コミット経路で `"SerializedSekibanExecutor"` が書き込まれます。

実際の呼び出し元を記録するには、`IExecutedUserProvider` を実装して DI に登録します。標準的なパターンは、呼び出しごとにアンビエント HTTP コンテキストを読み取る singleton プロバイダーです。

```csharp
public class HttpContextExecutedUserProvider : IExecutedUserProvider
{
    private readonly IHttpContextAccessor _accessor;
    public HttpContextExecutedUserProvider(IHttpContextAccessor accessor) => _accessor = accessor;
    public string GetExecutedUser() => _accessor.HttpContext?.User.Identity?.Name ?? "anonymous";
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IExecutedUserProvider>(sp =>
    new HttpContextExecutedUserProvider(sp.GetRequiredService<IHttpContextAccessor>()));
```

プロバイダーはコマンドごとに 1 回だけ評価され、そのコマンドが生成するすべてのイベントで同じ値が使われます。プロバイダーが未登録、または `null`/空文字を返した場合は `"GeneralSekibanExecutor"` にフォールバックします。

> **ライフタイムの指針。** `IExecutedUserProvider` は executor にキャプチャされます。プロバイダーが scoped または transient の場合、古いプロバイダーを保持しないよう executor も scoped または transient で登録してください。上記のアンビエント HTTP コンテキスト方式ではプロバイダーが singleton なので、executor も singleton にできます。

プロバイダーは `GeneralSekibanExecutor`、`OrleansDcbExecutor`、`InMemoryDcbExecutor` およびそれらの WithoutResult/テスト用版のコンストラクタで省略可能な引数として受け取れるため、既存の呼び出し元に影響はありません。

このように、DCB ではタグを中心にドメインを建て付けます。タグ定義・プロジェクター・コマンドハンドラーを
組み合わせて一貫した整合性境界を実現してください。
