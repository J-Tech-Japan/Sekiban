# AOT対応 プロジェクト再構成 設計書

## 0. 設計原則

### 0.1 ICore系の役割

`ICore` 系インターフェース（`ICoreMultiProjector`, `ICoreQueryTypes`, `ICoreCommandContext` など）は **共用のための抽象化層** として存在する。

- **WithResult** では `IMultiProjector<T>` = `ICoreMultiProjector<T>` のエイリアス
- **WithoutResult** では `IMultiProjector<T>` を独自に定義（例外ベース）
- 両者は同じ `Core` 実装を共有できる

### 0.2 Model層の必要性

AOT対応を完全にするには、ドメイン定義に必要なインターフェースを **Model層** に分離する必要がある：

- `ICommand`, `ICommandHandler` 系
- `IMultiProjector` 系
- `IMultiProjectionQuery`, `IMultiProjectionListQuery` 系

これにより、ユーザーは実装層（Core, Orleans等）に依存せずにドメインを定義できる。

## 1. 現状の課題

### 1.1 問題点

現在の構造では以下の課題がある：

1. **Core.Model が ResultBoxes に依存**
   - `Sekiban.Dcb.Core.Model` は `IsAotCompatible=true` を宣言
   - しかし `ICoreMultiProjector<T>.Project()` が `ResultBox<T>` を返す
   - WithoutResult版で「Resultを使わないドメイン定義」をしたくても、Core.Model を参照すると ResultBoxes が付いてくる

2. **Command系インターフェースが Core に存在**
   - `ICommand` は `Sekiban.Dcb.Core` にある
   - `ICommandHandler` は `WithResult`/`WithoutResult` にある
   - ドメイン定義のためだけに Core を参照する必要がある

3. **WithoutResult版の独立性が不十分**
   - `IMultiProjector<T>` を `Sekiban.Dcb.WithoutResult` で定義しているが、内部で `Sekiban.Dcb.Core` を参照
   - Core → Core.Model → ResultBoxes という依存チェーン

4. **AOTでの選択肢が限定的**
   - AOTユーザーが「ResultBoxなし」でドメインを定義したい場合の正式なサポートがない

## 2. 設計方針

### 2.1 新しいプロジェクト階層

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        ドメイン定義層（ユーザー選択）                            │
│                                                                             │
│   ┌──────────────────────────┐     ┌──────────────────────────┐            │
│   │ Sekiban.Dcb.             │     │ Sekiban.Dcb.             │            │
│   │ WithResult.Model         │     │ WithoutResult.Model      │  ← NEW     │
│   │ (ResultBox API)          │     │ (Exception API)          │            │
│   │                          │     │                          │            │
│   │ - IMultiProjector<T>     │     │ - IMultiProjector<T>     │            │
│   │   (= ICoreMultiProjector)│     │   (例外ベース)            │            │
│   │ - ICommandHandler<T>     │     │ - ICommandHandler<T>     │            │
│   │   (ResultBox返却)         │     │   (例外ベース)            │            │
│   │ - IMultiProjectionQuery  │     │ - IMultiProjectionQuery  │            │
│   └──────────┬───────────────┘     └──────────┬───────────────┘            │
│              │                                │                            │
│              └────────────┬───────────────────┘                            │
│                          │                                                │
│              ┌───────────▼───────────┐                                    │
│              │ Sekiban.Dcb.Core.Model │                                    │
│              │ (共通型・ICore系)       │                                    │
│              │ IsAotCompatible=true   │                                    │
│              │                        │                                    │
│              │ - ICommand (マーカー)   │  ← Core から移動                   │
│              │ - ICoreMultiProjector  │                                    │
│              │ - ICoreCommandContext  │                                    │
│              │ - Event, ITag, etc.    │                                    │
│              │ - ResultBoxes依存あり   │  ← ICore系のため                   │
│              └───────────────────────┘                                    │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 依存関係の変更

```
【変更前】
Core (ICommand等) → Core.Model (ResultBoxes依存) ← WithResult/WithoutResult

【変更後】
Core.Model (ICommand, ICore系, ResultBoxes依存)
    ↑
    ├── WithResult.Model (IMultiProjector = ICoreMultiProjector のエイリアス)
    │       ↑
    │       └── WithResult (実装)
    │
    ├── WithoutResult.Model (IMultiProjector 独自定義, ResultBox無し)  ← NEW
    │       ↑
    │       └── WithoutResult (実装)
    │
    └── Core (ICoreCommandContext実装等)
```

### 2.3 ICore系の共用パターン

```csharp
// Core.Model: ICore系を定義（ResultBox依存）
public interface ICoreMultiProjector<T> {
    static abstract ResultBox<T> Project(...);
}

public interface ICoreCommandContext {
    Task<ResultBox<TagState>> GetStateAsync<TProjector>(ITag tag);
    Task<ResultBox<EventOrNone>> AppendEvent(IEventPayload ev, params ITag[] tags);
}

// WithResult.Model: エイリアスとして公開
public interface IMultiProjector<T> : ICoreMultiProjector<T> { }
public interface ICommandContext : ICoreCommandContext { }

// WithoutResult.Model: 例外ベースで独自定義
public interface IMultiProjector<T> {
    static abstract T Project(...);  // ResultBox無し
}

public interface ICommandContext {
    Task<TagState> GetStateAsync<TProjector>(ITag tag);  // ResultBox無し
    Task<EventOrNone> AppendEvent(IEventPayload ev, params ITag[] tags);
}
```

## 3. Core.Model の変更（ICommand移動・ICore系維持）

### 3.1 ICommand を Core から Core.Model へ移動

```csharp
// Sekiban.Dcb.Core.Model/Commands/ICommand.cs
namespace Sekiban.Dcb.Core.Model.Commands;

/// <summary>
/// Marker interface for commands.
/// All commands must implement this interface.
/// </summary>
public interface ICommand
{
}
```

### 3.2 ICoreCommandContext を Core.Model に追加

```csharp
// Sekiban.Dcb.Core.Model/Commands/ICoreCommandContext.cs
namespace Sekiban.Dcb.Core.Model.Commands;

/// <summary>
/// Core command context interface with ResultBox-based error handling.
/// This is the foundation that both WithResult and WithoutResult build upon.
/// </summary>
public interface ICoreCommandContext
{
    Task<ResultBox<TagStateTyped<TState>>> GetStateAsync<TState, TProjector>(ITag tag)
        where TState : ITagStatePayload
        where TProjector : ITagProjector<TProjector>;

    Task<ResultBox<TagState>> GetStateAsync<TProjector>(ITag tag)
        where TProjector : ITagProjector<TProjector>;

    Task<ResultBox<bool>> TagExistsAsync(ITag tag);

    Task<ResultBox<string>> GetTagLatestSortableUniqueIdAsync(ITag tag);

    Task<ResultBox<EventOrNone>> AppendEvent(IEventPayload ev, params ITag[] tags);

    Task<ResultBox<EventOrNone>> AppendEvent(EventPayloadWithTags eventPayloadWithTags);
}
```

### 3.3 プロジェクト設定（変更なし）

Core.Model は引き続き ResultBoxes に依存する（ICore系のため）。

```xml
<!-- Sekiban.Dcb.Core.Model.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ResultBoxes" Version="0.4.0" />
  </ItemGroup>
</Project>
```

## 4. 新規プロジェクト: Sekiban.Dcb.WithResult.Model

### 4.1 プロジェクト設定

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <IsAotCompatible>true</IsAotCompatible>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../Sekiban.Dcb.Core.Model/Sekiban.Dcb.Core.Model.csproj" />
    <!-- ResultBoxes は Core.Model 経由で取得 -->
  </ItemGroup>
</Project>
```

### 4.2 IMultiProjector（ICoreのエイリアス）

```csharp
// Sekiban.Dcb.WithResult.Model/MultiProjections/IMultiProjector.cs
namespace Sekiban.Dcb.WithResult.Model.MultiProjections;

/// <summary>
/// Multi-projector interface with ResultBox-based error handling.
/// This is an alias for ICoreMultiProjector for API consistency.
/// </summary>
public interface IMultiProjector<T> : ICoreMultiProjector<T> where T : IMultiProjector<T>
{
}
```

### 4.3 ICommandHandler（ResultBox版）

```csharp
// Sekiban.Dcb.WithResult.Model/Commands/ICommandHandler.cs
namespace Sekiban.Dcb.WithResult.Model.Commands;

/// <summary>
/// Command handler interface with ResultBox-based error handling.
/// </summary>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    static abstract Task<ResultBox<EventOrNone>> HandleAsync(
        TCommand command,
        ICommandContext context);
}
```

### 4.4 ICommandContext（ICoreのエイリアス）

```csharp
// Sekiban.Dcb.WithResult.Model/Commands/ICommandContext.cs
namespace Sekiban.Dcb.WithResult.Model.Commands;

/// <summary>
/// Command context interface with ResultBox-based error handling.
/// This is an alias for ICoreCommandContext.
/// </summary>
public interface ICommandContext : ICoreCommandContext
{
}
```

## 5. 新規プロジェクト: Sekiban.Dcb.WithoutResult.Model

### 5.1 プロジェクト設定

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <IsAotCompatible>true</IsAotCompatible>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../Sekiban.Dcb.Core.Model/Sekiban.Dcb.Core.Model.csproj" />
    <!-- ResultBoxes への直接依存なし（Core.Model経由で参照は可能だがAPIでは使わない） -->
  </ItemGroup>
</Project>
```

### 5.2 インターフェース定義

#### 5.2.1 IMultiProjector（Exception-based）

```csharp
// Sekiban.Dcb.WithoutResult.Model/MultiProjections/IMultiProjector.cs
namespace Sekiban.Dcb.WithoutResult.Model.MultiProjections;

/// <summary>
/// Multi-projector interface for AOT-compatible, exception-based projections.
/// </summary>
/// <typeparam name="T">The projector type</typeparam>
public interface IMultiProjector<T> : IMultiProjectionPayload where T : IMultiProjector<T>
{
    /// <summary>
    /// The unique name of this multi-projector
    /// </summary>
    static abstract string MultiProjectorName { get; }

    /// <summary>
    /// The version of this multi-projector (used for state management)
    /// </summary>
    static abstract string MultiProjectorVersion { get; }

    /// <summary>
    /// Project an event onto the current state.
    /// Throws exception on error (no ResultBox).
    /// </summary>
    /// <param name="payload">Current state</param>
    /// <param name="ev">Event to project</param>
    /// <param name="tags">Tags associated with the event</param>
    /// <param name="domainTypes">Domain type registry</param>
    /// <param name="safeWindowThreshold">Safe window threshold for state management</param>
    /// <returns>Updated state</returns>
    /// <exception cref="ProjectionException">When projection fails</exception>
    static abstract T Project(
        T payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold);

    /// <summary>
    /// Generate the initial payload state
    /// </summary>
    /// <returns>Initial state instance</returns>
    static abstract T GenerateInitialPayload();
}
```

#### 5.2.2 ICommandHandler（Exception-based）

```csharp
// Sekiban.Dcb.WithoutResult.Model/Commands/ICommandHandler.cs
namespace Sekiban.Dcb.WithoutResult.Model.Commands;

/// <summary>
/// Command handler interface with exception-based error handling.
/// </summary>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    /// <summary>
    /// Handle the command and return event(s) to be appended.
    /// Throws exception on error (no ResultBox).
    /// </summary>
    static abstract Task<EventOrNone> HandleAsync(
        TCommand command,
        ICommandContext context);
}
```

#### 5.2.3 ICommandContext（Exception-based）

```csharp
// Sekiban.Dcb.WithoutResult.Model/Commands/ICommandContext.cs
namespace Sekiban.Dcb.WithoutResult.Model.Commands;

/// <summary>
/// Command context interface with exception-based error handling.
/// Unlike ICoreCommandContext, this does not use ResultBox.
/// </summary>
public interface ICommandContext
{
    Task<TagStateTyped<TState>> GetStateAsync<TState, TProjector>(ITag tag)
        where TState : ITagStatePayload
        where TProjector : ITagProjector<TProjector>;

    Task<TagState> GetStateAsync<TProjector>(ITag tag)
        where TProjector : ITagProjector<TProjector>;

    Task<bool> TagExistsAsync(ITag tag);

    Task<string> GetTagLatestSortableUniqueIdAsync(ITag tag);

    Task<EventOrNone> AppendEvent(IEventPayload ev, params ITag[] tags);

    Task<EventOrNone> AppendEvent(EventPayloadWithTags eventPayloadWithTags);
}
```

#### 5.2.4 IMultiProjectionListQuery（Exception-based）

```csharp
// Sekiban.Dcb.WithoutResult.Model/Queries/IMultiProjectionListQuery.cs
namespace Sekiban.Dcb.WithoutResult.Model.Queries;

/// <summary>
/// List query interface for AOT-compatible, exception-based queries.
/// </summary>
public interface IMultiProjectionListQuery<TMultiProjector, TQuery, TOutput> :
    IListQueryCommon<TQuery, TOutput>,
    IQueryPagingParameter
    where TMultiProjector : IMultiProjector<TMultiProjector>
    where TQuery : IMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>
{
    /// <summary>
    /// Filter the projection to get the items.
    /// Throws exception on error.
    /// </summary>
    static abstract IEnumerable<TOutput> HandleFilter(
        TMultiProjector projector,
        TQuery query,
        IQueryContext context);

    /// <summary>
    /// Sort the filtered items.
    /// Throws exception on error.
    /// </summary>
    static abstract IEnumerable<TOutput> HandleSort(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);
}
```

#### 5.2.5 IMultiProjectionQuery（単一結果）

```csharp
// Sekiban.Dcb.WithoutResult.Model/Queries/IMultiProjectionQuery.cs
namespace Sekiban.Dcb.WithoutResult.Model.Queries;

/// <summary>
/// Single-item query interface for AOT-compatible, exception-based queries.
/// </summary>
public interface IMultiProjectionQuery<TMultiProjector, TQuery, TOutput> :
    IQueryCommon<TQuery, TOutput>
    where TMultiProjector : IMultiProjector<TMultiProjector>
    where TQuery : IMultiProjectionQuery<TMultiProjector, TQuery, TOutput>
{
    /// <summary>
    /// Handle the query and return a single result.
    /// Throws exception on error.
    /// </summary>
    static abstract TOutput HandleQuery(
        TMultiProjector projector,
        TQuery query,
        IQueryContext context);
}
```

### 5.3 AOT用の型登録

```csharp
// Sekiban.Dcb.WithoutResult.Model/Domains/AotWithoutResultMultiProjectorTypes.cs
namespace Sekiban.Dcb.WithoutResult.Model.Domains;

/// <summary>
/// AOT-compatible projector type registry for exception-based projectors.
/// </summary>
public sealed class AotWithoutResultMultiProjectorTypes : IWithoutResultMultiProjectorTypes
{
    private readonly Dictionary<string, ProjectorRegistration> _projectors = new();

    /// <summary>
    /// Register a projector with its JSON serialization info.
    /// </summary>
    public void RegisterProjector<T>(JsonTypeInfo<T> typeInfo)
        where T : IMultiProjector<T>
    {
        var name = T.MultiProjectorName;
        _projectors[name] = new ProjectorRegistration(
            ProjectorName: name,
            ProjectorVersion: T.MultiProjectorVersion,
            PayloadType: typeof(T),
            // Exception-based Project delegate
            Project: (payload, ev, tags, domainTypes, threshold) =>
                T.Project((T)payload, ev, tags, domainTypes, threshold),
            GenerateInitial: () => T.GenerateInitialPayload(),
            Serialize: (payload, options) =>
                JsonSerializer.Serialize((T)payload, typeInfo),
            Deserialize: (json) =>
                JsonSerializer.Deserialize(json, typeInfo)!
        );
    }

    /// <summary>
    /// Project an event using the registered projector.
    /// </summary>
    public IMultiProjectionPayload Project(
        string projectorName,
        IMultiProjectionPayload payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        if (!_projectors.TryGetValue(projectorName, out var registration))
            throw new InvalidOperationException($"Projector '{projectorName}' not registered");

        return registration.Project(payload, ev, tags, domainTypes, safeWindowThreshold);
    }
}
```

## 6. 既存プロジェクトの調整

### 6.1 参照変更まとめ

| プロジェクト | 変更前の参照 | 変更後の参照 |
|------------|------------|-------------|
| Sekiban.Dcb.Core.Model | ResultBoxes | ResultBoxes（変更なし） |
| Sekiban.Dcb.WithResult.Model (NEW) | - | Core.Model |
| Sekiban.Dcb.WithoutResult.Model (NEW) | - | Core.Model |
| Sekiban.Dcb.Core | Core.Model, ICommand定義 | Core.Model（ICommand移動） |
| Sekiban.Dcb.WithResult | Core, ICommandHandler定義 | Core, WithResult.Model |
| Sekiban.Dcb.WithoutResult | Core, ICommandHandler定義 | Core, WithoutResult.Model |

### 6.2 DcbDomainTypes の拡張

```csharp
// Sekiban.Dcb.Core.Model/DcbDomainTypes.cs
public record DcbDomainTypes
{
    public IEventTypes EventTypes { get; init; }
    public ITagTypes TagTypes { get; init; }
    public ITagProjectorTypes TagProjectorTypes { get; init; }
    public ITagStatePayloadTypes TagStatePayloadTypes { get; init; }

    // WithResult用（ICore系）
    public ICoreMultiProjectorTypes MultiProjectorTypes { get; init; }
    public ICoreQueryTypes QueryTypes { get; init; }

    // WithoutResult用（オプショナル - AOT時に使用）
    public IWithoutResultMultiProjectorTypes? WithoutResultMultiProjectorTypes { get; init; }
    public IWithoutResultQueryTypes? WithoutResultQueryTypes { get; init; }

    public JsonSerializerOptions JsonSerializerOptions { get; init; }
}
```

## 7. AOT使用例

### 7.1 WithoutResult版でのドメイン定義（AOT推奨）

```csharp
// ユーザードメイン定義（ResultBoxなし）
using Sekiban.Dcb.Core.Model.Commands;
using Sekiban.Dcb.Core.Model.Events;
using Sekiban.Dcb.WithoutResult.Model.Commands;
using Sekiban.Dcb.WithoutResult.Model.MultiProjections;
using Sekiban.Dcb.WithoutResult.Model.Queries;

// イベント定義
public record UserCreated(string UserId, string Name) : IEventPayload;
public record UserDeleted(string UserId) : IEventPayload;

// コマンド定義（ICommand は Core.Model から）
public record CreateUserCommand(string UserId, string Name) : ICommand;

// コマンドハンドラー（例外ベース）
public record CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    public static async Task<EventOrNone> HandleAsync(
        CreateUserCommand command,
        ICommandContext context)
    {
        // 例外をスロー（ResultBox不使用）
        var exists = await context.TagExistsAsync(new UserTag(command.UserId));
        if (exists)
            throw new InvalidOperationException($"User {command.UserId} already exists");

        return EventOrNone.Event(
            new UserCreated(command.UserId, command.Name),
            new UserTag(command.UserId));
    }
}

// プロジェクター定義（例外ベース）
public record UserListProjector : IMultiProjector<UserListProjector>
{
    public static string MultiProjectorName => "UserList";
    public static string MultiProjectorVersion => "v1";

    public Dictionary<string, User> Users { get; init; } = new();

    public static UserListProjector Project(
        UserListProjector payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        return ev.Payload switch
        {
            UserCreated created => payload with
            {
                Users = payload.Users.SetItem(created.UserId, new User(created.UserId, created.Name))
            },
            UserDeleted deleted => payload with
            {
                Users = payload.Users.Remove(deleted.UserId)
            },
            _ => payload
        };
    }

    public static UserListProjector GenerateInitialPayload() => new();
}

// クエリ定義（例外ベース）
public record GetAllUsersQuery : IMultiProjectionListQuery<UserListProjector, GetAllUsersQuery, User>
{
    public int? PageSize { get; init; }
    public int? PageNumber { get; init; }

    public static IEnumerable<User> HandleFilter(
        UserListProjector projector,
        GetAllUsersQuery query,
        IQueryContext context)
    {
        return projector.Users.Values;
    }

    public static IEnumerable<User> HandleSort(
        IEnumerable<User> filteredList,
        GetAllUsersQuery query,
        IQueryContext context)
    {
        return filteredList.OrderBy(u => u.Name);
    }
}
```

### 7.2 WithResult版でのドメイン定義（従来どおり）

```csharp
using Sekiban.Dcb.Core.Model.Commands;
using Sekiban.Dcb.WithResult.Model.Commands;
using Sekiban.Dcb.WithResult.Model.MultiProjections;

// コマンドハンドラー（ResultBox版）
public record CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    public static async Task<ResultBox<EventOrNone>> HandleAsync(
        CreateUserCommand command,
        ICommandContext context)
    {
        return await context.TagExistsAsync(new UserTag(command.UserId))
            .Conveyor(exists => exists
                ? ResultBox<EventOrNone>.FromException(
                    new InvalidOperationException($"User {command.UserId} already exists"))
                : EventOrNone.Event(
                    new UserCreated(command.UserId, command.Name),
                    new UserTag(command.UserId)));
    }
}

// プロジェクター定義（ResultBox版）
public record UserListProjector : IMultiProjector<UserListProjector>
{
    public static string MultiProjectorName => "UserList";
    public static string MultiProjectorVersion => "v1";

    public Dictionary<string, User> Users { get; init; } = new();

    public static ResultBox<UserListProjector> Project(
        UserListProjector payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        return ev.Payload switch
        {
            UserCreated created => ResultBox.FromValue(payload with
            {
                Users = payload.Users.SetItem(created.UserId, new User(created.UserId, created.Name))
            }),
            UserDeleted deleted => ResultBox.FromValue(payload with
            {
                Users = payload.Users.Remove(deleted.UserId)
            }),
            _ => ResultBox.FromValue(payload)
        };
    }

    public static UserListProjector GenerateInitialPayload() => new();
}
```

### 7.3 AOT登録

```csharp
// Program.cs (AOT対応アプリ)

// WithoutResult版
var builder = new AotDomainTypesBuilder();
builder.EventTypes.Register("UserCreated", UserCreatedContext.Default.UserCreated);
builder.EventTypes.Register("UserDeleted", UserDeletedContext.Default.UserDeleted);
builder.WithoutResultMultiProjectorTypes.RegisterProjector(UserListProjectorContext.Default.UserListProjector);
builder.WithoutResultQueryTypes.RegisterListQuery<UserListProjector, GetAllUsersQuery, User>(
    GetAllUsersQueryContext.Default.GetAllUsersQuery);

var domainTypes = builder.Build();

// または WithResult版
builder.MultiProjectorTypes.RegisterProjector(UserListProjectorContext.Default.UserListProjector);
```

## 8. ファイル構成

```
dcb/src/
├── Sekiban.Dcb.Core.Model/           # 共通型（ICore系, ResultBox依存あり）
│   ├── Commands/
│   │   ├── ICommand.cs               # ← Core から移動
│   │   └── ICoreCommandContext.cs    # ← Core から移動
│   ├── Events/
│   │   ├── IEventPayload.cs
│   │   ├── Event.cs
│   │   └── EventOrNone.cs
│   ├── Tags/
│   │   ├── ITag.cs
│   │   └── ITagProjector.cs
│   ├── MultiProjections/
│   │   ├── IMultiProjectionPayload.cs
│   │   └── ICoreMultiProjector.cs
│   ├── Queries/
│   │   ├── IQueryContext.cs
│   │   ├── ICoreMultiProjectionQuery.cs
│   │   ├── ICoreMultiProjectionListQuery.cs
│   │   └── ICoreQueryTypes.cs
│   └── Domains/
│       ├── DcbDomainTypes.cs
│       ├── AotDomainTypesBuilder.cs
│       ├── AotEventTypes.cs
│       ├── AotMultiProjectorTypes.cs  # ICore系用
│       └── AotQueryTypes.cs           # ICore系用
│
├── Sekiban.Dcb.WithResult.Model/     # NEW: ResultBox API のモデル定義
│   ├── Commands/
│   │   ├── ICommandHandler.cs        # ResultBox<EventOrNone> を返す
│   │   ├── ICommandWithHandler.cs
│   │   └── ICommandContext.cs        # = ICoreCommandContext エイリアス
│   ├── MultiProjections/
│   │   └── IMultiProjector.cs        # = ICoreMultiProjector エイリアス
│   └── Queries/
│       ├── IMultiProjectionQuery.cs  # = ICoreMultiProjectionQuery エイリアス
│       └── IMultiProjectionListQuery.cs
│
├── Sekiban.Dcb.WithoutResult.Model/  # NEW: Exception API のモデル定義
│   ├── Commands/
│   │   ├── ICommandHandler.cs        # EventOrNone を返す（例外ベース）
│   │   ├── ICommandWithHandler.cs
│   │   └── ICommandContext.cs        # 独自定義（ResultBox無し）
│   ├── MultiProjections/
│   │   └── IMultiProjector.cs        # 独自定義（T を返す、例外ベース）
│   ├── Queries/
│   │   ├── IMultiProjectionQuery.cs  # 独自定義（例外ベース）
│   │   └── IMultiProjectionListQuery.cs
│   └── Domains/
│       ├── AotWithoutResultMultiProjectorTypes.cs
│       └── AotWithoutResultQueryTypes.cs
│
├── Sekiban.Dcb.Core/                 # 実装レイヤー
│   ├── Commands/
│   │   ├── CoreGeneralCommandContext.cs  # ICoreCommandContext 実装
│   │   └── ICommandContextResultAccessor.cs
│   └── Actors/
│       └── CoreGeneralSekibanExecutor.cs
│
├── Sekiban.Dcb.WithResult/           # ResultBox API 実装
│   ├── Commands/
│   │   ├── CommandContextAdapter.cs  # ICoreCommandContext → ICommandContext
│   │   └── ICommandExecutor.cs
│   └── Actors/
│       └── GeneralSekibanExecutor.cs
│
└── Sekiban.Dcb.WithoutResult/        # Exception API 実装
    ├── Commands/
    │   ├── CommandContextAdapter.cs  # ICoreCommandContext → ICommandContext (UnwrapBox)
    │   └── ICommandExecutor.cs
    └── Actors/
        └── GeneralSekibanExecutor.cs
```

## 9. マイグレーションパス

### 9.1 既存ユーザー向け

1. **WithResult ユーザー**:
   - `using Sekiban.Dcb.WithResult.Commands` → `using Sekiban.Dcb.WithResult.Model.Commands`
   - `using Sekiban.Dcb.Core.Model.MultiProjections` → `using Sekiban.Dcb.WithResult.Model.MultiProjections`
   - `ICommand` は `Sekiban.Dcb.Core.Model.Commands` から
   - 動作は同一（エイリアスのため破壊的変更なし）

2. **WithoutResult ユーザー**:
   - `using Sekiban.Dcb.WithoutResult.Commands` → `using Sekiban.Dcb.WithoutResult.Model.Commands`
   - `using Sekiban.Dcb.WithoutResult.MultiProjections` → `using Sekiban.Dcb.WithoutResult.Model.MultiProjections`
   - `ICommand` は `Sekiban.Dcb.Core.Model.Commands` から
   - 動作は同一（インターフェースは同じシグネチャ）

### 9.2 新規AOTユーザー向け

- **ResultBox を使う場合**:
  - `Sekiban.Dcb.WithResult.Model` を参照
  - `IMultiProjector<T>` = `ICoreMultiProjector<T>` のエイリアスで統一されたAPI

- **ResultBox を使わない場合（AOT推奨）**:
  - `Sekiban.Dcb.WithoutResult.Model` を参照
  - 例外ベースのシンプルなAPI
  - Core.Model は参照されるが、ユーザーコードで ResultBox を使う必要なし

## 10. 実装優先順位

1. **Phase 1**: Core から ICommand, ICoreCommandContext を Core.Model に移動
2. **Phase 2**: `Sekiban.Dcb.WithResult.Model` プロジェクト作成
   - IMultiProjector, ICommandHandler, ICommandContext などを ICoreの エイリアスとして定義
3. **Phase 3**: `Sekiban.Dcb.WithoutResult.Model` プロジェクト作成
   - 例外ベースの独自インターフェースを定義
4. **Phase 4**: 既存の WithResult/WithoutResult プロジェクトの参照更新
   - Commands, MultiProjections などを Model から re-export
5. **Phase 5**: テスト・ドキュメント更新

## 11. オープン課題

1. **ITagProjector の配置**:
   - 現在 Core.Model にあり ResultBox を使っていないので移動不要
   - ただし将来的に WithResult/WithoutResult で分けるか検討

2. **Orleans 統合**:
   - Orleans grain の型制約をどちらの Model に依存させるか
   - 現状: `Sekiban.Dcb.Orleans.Core` → `Sekiban.Dcb.Core` → `Core.Model`
   - 提案: Orleans.Core は Core.Model の ICore系を使い、WithResult/WithoutResult.Orleans は対応する Model を使う

3. **カスタムシリアライゼーション**:
   - `IMultiProjectorWithCustomSerialization<T>` の WithoutResult 版が必要か
   - AOT では JsonTypeInfo ベースなので、カスタムシリアライゼーションも対応必要

4. **後方互換性**:
   - 既存の `Sekiban.Dcb.WithResult` と `Sekiban.Dcb.WithoutResult` から
     インターフェースを re-export して既存コードを壊さないようにする
   - 例: `namespace Sekiban.Dcb.WithResult.Commands { public interface ICommandHandler<T> : Model.Commands.ICommandHandler<T> { } }`

## 12. 設計の利点

### 12.1 AOTユーザー向け

- **WithoutResult.Model のみ参照** で完全にResultBox無しのドメイン定義が可能
- JsonTypeInfo ベースの AOT 登録
- 例外ベースのシンプルなエラーハンドリング

### 12.2 既存ユーザー向け

- **破壊的変更なし** - 既存のインターフェースはエイリアスで維持
- 段階的な移行が可能

### 12.3 フレームワーク開発者向け

- **ICore系で共通実装** - Core の実装は両方のAPIで共有
- **アダプタパターン** - WithoutResult は `UnwrapBox()` でResultBoxを例外に変換
