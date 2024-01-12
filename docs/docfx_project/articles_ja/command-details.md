# コマンド詳細

Sekiban用のコマンドを作成する際、いくつかのインターフェースを実装することができます。以下に、コマンドの種類を示します。

コマンドを使用するためには、それを `DependencyDefinition` に追加する必要があります。

```cs
public class DomainDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();

    public override void Define()
    {
        // add aggregate
        AddAggregate<UserPoint>()
            // add command with command handler.
            .AddCommandHandler<CreateUserPoint, CreateUserPoint.Handler>()
    }
}
```

## コマンドの種類

### `ICommand<AggregateType>`

- `GetAggregateId` であなたのAggregateId（Guid）を返す必要があります。新しいアグリゲートを作成するために `Guid.NewGuid()` または同じアグリゲートに存在しないGuidを返すことができます。
- `ICommandHandler<AggregateType, CommandType>` または `ICommandHandlerAsync<AggregateType, CommandType>` を実装することでコマンドハンドラーを宣言できます（非同期のコマンドハンドラーを使用したい場合）。
- YES. 新しいアグリゲートの作成に使用できます。
- YES. 既存のアグリゲートに使用できます。
- NO. コマンド実行者はバージョン検証を実行しません。
- YES. コマンド実行者は現在のアグリゲートをロードし、それをコンテキストに渡します。 `context.GetState()` で現在のAggregateStateにアクセスできます。まだイベントが存在しない場合、 `context.GetState().IsNew` はtrueを返します。
- NO. テナント機能はありませんが、手動でRoot Partition Keyを返す `GetRootPartitionKey()` を使用できます。

### `ICommandForExistingAggregate<AggregateType>`

- `GetAggregateId` であなたのAggregateId（Guid）を返す必要があります。少なくとも一つのイベントを既に保存しているAggregateIdを返す必要があります。例） `public Guid GetAggregateId() => ClientId; `
- `ICommandHandler<AggregateType, CommandType>` または `ICommandHandlerAsync<AggregateType, CommandType>` を実装することでコマンドハンドラーを宣言できます（非同期のコマンドハンドラーを使用したい場合）。
- NO. 新しいアグリゲートの作成には使用できません。 `SekibanAggregateNotExistsException` をスローします。
- YES. 既存のアグリゲートに使用できます。
- NO. コマンド実行者はバージョン検証を実行しません。
- YES. コマンド実行者は現在のアグリゲートをロードし、それをコンテキストに渡します。 `context.GetState()` で現在のAggregateStateにアクセスできます。まだイベントが存在しない場合、コマンドハンドラーは呼び出されません。
- NO. テナント機能はありませんが、手動でRoot Partition Keyを返す `GetRootPartitionKey()` を使用できます。

### `ICommandWithVersionValidation<AggregateType>`

- `GetAggregateId` であなたのAggregateId（Guid）を返す必要があります。新しいアグリゲートを作成するために `Guid.NewGuid()` または同じアグリゲートに存在しないGuidを返すことができます。
- `ICommandHandler<AggregateType, CommandType>` または `ICommandHandlerAsync<AggregateType, CommandType>` を実装することでコマンドハンドラーを宣言できます（非同期のコマンドハンドラーを使用したい場合）。
- YES. 新しいアグリゲートの作成に使用できます。アグリゲートがまだ作成されていないことを確認したい場合は、 `ReferenceVersion` を0に設定します。
- YES. 既存のアグリゲートに使用できます。
- YES. コマンド実行者はバージョン検証を実行します。参照する `ReferenceVersion` を渡す必要があります。現在のバージョンがReferenceVersionと異なる場合、 `SekibanCommandInconsistentVersionException` をスローします。
- YES. コマンド実行者は現在のアグリゲートをロードし、それをコンテキストに渡します。 `context.GetState()` で現在のAggregateStateにアクセスできます。まだイベントが存在しない場合、 `context.GetState().IsNew` はtrueを返します。
- NO. テナント機能はありませんが、手動でRoot Partition Keyを返す `GetRootPartitionKey()` を使用できます。

### `ICommandWithVersionValidationForExistingAggregate<AggregateType>`

- `GetAggregateId` であなたのAggregateId（Guid）を返す必要があります。少なくとも一つのイベントを既に保存しているAggregateIdを返す必要があります。例） `public Guid GetAggregateId() => ClientId; `
- `ICommandHandler<AggregateType, CommandType>` または `ICommandHandlerAsync<AggregateType, CommandType>` を実装することでコマンドハンドラーを宣言できます（非同期のコマンドハンドラーを使用したい場合）。
- NO. 新しいアグリゲートの作成には使用できません。 `SekibanAggregateNotExistsException` をスローします。
- YES. 既存のアグリゲートに使用できます。
- YES. コマンド実行者はバージョン検証を実行します。参照する `ReferenceVersion` を渡す必要があります。現在のバージョンがReferenceVersionと異なる場合、 `SekibanCommandInconsistentVersionException` をスローします。
- YES. コマンド実行者は現在のアグリゲートをロードし、それをコンテキストに渡します。 `context.GetState()` で現在のAggregateStateにアクセスできます。まだイベントが存在しない場合、コマンドハンドラーは呼び出されません。
- NO. テナント機能はありませんが、手動でRoot Partition Keyを返す `GetRootPartitionKey()` を使用できます。

### `ICommandWithoutLoadingAggregate<AggregateType>`

- `GetAggregateId` であなたのAggregateId（Guid）を返す必要があります。少なくとも一つのイベントを既に保存しているAggregateIdを返す必要があります。例） `public Guid GetAggregateId() => ClientId; `
- `ICommandWithoutLoadingAggregateHandler<AggregateType, CommandType>` または `ICommandWithoutLoadingAggregateHandlerAsync<AggregateType, CommandType>` を実装することでコマンドハンドラーを宣言できます（非同期のコマンドハンドラーを使用したい場合）。
- YES. 新しいアグリゲートの作成に使用できます。
- YES. 既存のアグリゲートに使用できます。
- NO. コマンド実行者はバージョン検証を実行しません。
- NO. コマンド実行者は現在のアグリゲートをロードせずにコンテキストに渡します。コマンドハンドラーでそれを使用することはできますが、このタイプのコマンドはアグリゲートをロードせずにイベントを作成するためのものです。このコマンドタイプを使用すると、イベントの作成が速くなりますが、現在の状態を確認することはできません。
- NO. テナント機能はありませんが、手動でRoot Partition Keyを返す `GetRootPartitionKey()` を使用できます。

### `ITenantCommand<AggregateType>`

- `GetAggregateId`を返す必要があります。これはあなたのAggregateId（Guid）です。新しい集約を作成するためには、`Guid.NewGuid()`を返すか、同じ集約に存在しないGuidを返すことができます。
- `ICommandHandler<AggregateType, CommandType>`または`ICommandHandlerAsync<AggregateType, CommandType>`を実装することで、コマンドハンドラを宣言できます。
- はい。新しい集約の作成に使用できます。
- はい。既存の集約に使用できます。
- いいえ。コマンド実行者はバージョン検証を行いません。
- はい。コマンド実行者は現在の集約をロードし、それをコンテキストに渡します。`context.GetState()`で現在のAggregateStateにアクセスできます。まだイベントが存在しない場合、`context.GetState().IsNew`はtrueを返します。
- はい。テナント機能があります。TenantIdを設定すると、それがRoot Partition Keyになります。

### `ITenantCommandForExistingAggregate<AggregateType>`

- `GetAggregateId`を返す必要があります。これはあなたのAggregateId（Guid）です。少なくとも一つのイベントを既に保存しているAggregateIdを返す必要があります。例）`public Guid GetAggregateId() => ClientId; `
- `ICommandHandler<AggregateType, CommandType>`または`ICommandHandlerAsync<AggregateType, CommandType>`を実装することで、コマンドハンドラを宣言できます。
- いいえ。新しい集約の作成には使用できません。`SekibanAggregateNotExistsException`がスローされます。
- はい。既存の集約に使用できます。
- いいえ。コマンド実行者はバージョン検証を行いません。
- はい。コマンド実行者は現在の集約をロードし、それをコンテキストに渡します。`context.GetState()`で現在のAggregateStateにアクセスできます。まだイベントが存在しない場合、コマンドハンドラは呼び出されません。
- はい。テナント機能があります。TenantIdを設定すると、それがRoot Partition Keyになります。

### `ITenantCommandWithVersionValidation<AggregateType>`

- `GetAggregateId`を返す必要があります。これはあなたのAggregateId（Guid）です。新しい集約を作成するためには、`Guid.NewGuid()`を返すか、同じ集約に存在しないGuidを返すことができます。
- `ICommandHandler<AggregateType, CommandType>`または`ICommandHandlerAsync<AggregateType, CommandType>`を実装することで、コマンドハンドラを宣言できます。
- はい。新しい集約の作成に使用できます。集約がまだ作成されていないことを確認したい場合は、`ReferenceVersion`に0を渡す必要があります。
- はい。既存の集約に使用できます。
- はい。コマンド実行者はバージョン検証を行います。参照する`ReferenceVersion`を渡す必要があります。現在のバージョンがReferenceVersionと異なる場合、`SekibanCommandInconsistentVersionException`がスローされます。
- はい。コマンド実行者は現在の集約をロードし、それをコンテキストに渡します。`context.GetState()`で現在のAggregateStateにアクセスできます。まだイベントが存在しない場合、`context.GetState().IsNew`はtrueを返します。
- はい。テナント機能があります。TenantIdを設定すると、それがRoot Partition Keyになります。

### `ITenantCommandWithVersionValidationForExistingAggregate<AggregateType>`

- `GetAggregateId`を返す必要があります。これはあなたのAggregateId（Guid）です。少なくとも一つのイベントを既に保存しているAggregateIdを返す必要があります。例）`public Guid GetAggregateId() => ClientId; `
- `ICommandHandler<AggregateType, CommandType>`または`ICommandHandlerAsync<AggregateType, CommandType>`を実装することで、コマンドハンドラを宣言できます。
- いいえ。新しい集約の作成には使用できません。`SekibanAggregateNotExistsException`がスローされます。
- はい。既存の集約に使用できます。
- はい。コマンド実行者はバージョン検証を行います。参照する`ReferenceVersion`を渡す必要があります。現在のバージョンがReferenceVersionと異なる場合、`SekibanCommandInconsistentVersionException`がスローされます。
- はい。コマンド実行者は現在の集約をロードし、それをコンテキストに渡します。`context.GetState()`で現在のAggregateStateにアクセスできます。まだイベントが存在しない場合、コマンドハンドラは呼び出されません。
- はい。テナント機能があります。TenantIdを設定すると、それがRoot Partition Keyになります。

### `ITenantCommandWithoutLoadingAggregate<AggregateType>`

- `GetAggregateId`を返す必要があります。これはあなたのAggregateId（Guid）です。少なくとも一つのイベントを既に保存しているAggregateIdを返す必要があります。例）`public Guid GetAggregateId() => ClientId; `
- `ICommandWithoutLoadingAggregateHandler<AggregateType, CommandType>`または`ICommandWithoutLoadingAggregateHandlerAsync<AggregateType, CommandType>`を実装することで、コマンドハンドラを宣言できます。
- はい。新しい集約の作成に使用できます。
- はい。既存の集約に使用できます。
- いいえ。コマンド実行者はバージョン検証を行いません。
- いいえ。コマンド実行者は現在の集約をロードせずにコンテキストに渡します。コマンドハンドラでそれを使用できますが、このタイプのコマンドは集約をロードせずにイベントを作成するためのものです。このコマンドタイプを使用すると、イベントの作成が速くなりますが、現在の状態を確認することはできません。
- はい。テナント機能があります。TenantIdを設定すると、それがRoot Partition Keyになります。

## コマンドのクリーンアップ

以下の理由でコマンド入力を保存する必要がない場合、コマンドをクリーンアップできます。
- 個人情報を含む。
- 保存するには長すぎる。

`ICleanupNecessaryCommand<TCommand>`インターフェースを追加し、`CleanupCommand`メソッドを実装すると、コマンドエクゼキュータはアイテムを保存する前に`CleanupCommand`を呼び出します。

```cs
public record CreateBranch : ICommand<Branch>, ICleanupNecessaryCommand<CreateBranch>
{

    [Required]
    [MaxLength(20)]
    public string Name { get; init; } = string.Empty;
    public CreateBranch() : this(string.Empty)
    {
    }

    public CreateBranch(string name) => Name = name;

    public CreateBranch CleanupCommand(CreateBranch command) => command with { Name = string.Empty };

    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandler<Branch, CreateBranch>
    {
        public IEnumerable<IEventPayloadApplicableTo<Branch>> HandleCommand(CreateBranch command, ICommandContext<Branch> context)
        {
            yield return new BranchCreated(command.Name);
        }
    }
}
```
