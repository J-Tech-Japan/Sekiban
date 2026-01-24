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

このように、DCB ではタグを中心にドメインを建て付けます。タグ定義・プロジェクター・コマンドハンドラーを
組み合わせて一貫した整合性境界を実現してください。
