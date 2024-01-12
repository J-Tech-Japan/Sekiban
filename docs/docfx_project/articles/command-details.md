# Command Details

When we create Command for Sekiban, you can implement interfaces. There are multiple types of Command, it will be following.

To register your command to use, you need to add it to `DependencyDefinition`
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

## Command types

### `ICommand<AggregateType>`

- You need to return `GetAggregateId` your AggregateId (Guid). You can return `Guid.NewGuid()` or Guid that is not exist in same aggregates to create new aggregate.
- You can declare Command Handler by implementing `ICommandHandler<AggregateType, CommandType>` or `ICommandHandlerAsync<AggregateType, CommandType>` if you want to use async command handler.
- YES. You can use for creating new aggregate.
- YES. You can use for existing aggregate.
- NO. Command executor does not run Version Validation.
- YES. Command executor load current aggregate and pass it to context. You can access with `context.GetState()` to access current AggregateState. When there is no event exists yet, `context.GetState().IsNew` will return true.
- NO. It does not have Tenant feature, but you can manually return your Root Partition Key `GetRootPartitionKey()`.

### `ICommandForExistingAggregate<AggregateType>`

- You need to return `GetAggregateId` your AggregateId (Guid). You need to return AggregateId that you already save at least one event. e.g.) `public Guid GetAggregateId() => ClientId; `
- You can declare Command Handler by implementing `ICommandHandler<AggregateType, CommandType>` or `ICommandHandlerAsync<AggregateType, CommandType>` if you want to use async command handler.
- NO. You can NOT use for creating new aggregate. It will throw `SekibanAggregateNotExistsException`
- YES. You can use for existing aggregate.
- NO. Command executor does not run Version Validation.
- YES. Command executor load current aggregate and pass it to context. You can access with `context.GetState()` to access current AggregateState. When there is no event exists yet, command handler will not be called.
- NO. It does not have Tenant feature, but you can manually return your Root Partition Key `GetRootPartitionKey()`.

### `ICommandWithVersionValidation<AggregateType>`

- You need to return `GetAggregateId` your AggregateId (Guid). You can return `Guid.NewGuid()` or Guid that is not exist in same aggregates to create new aggregate.
- You can declare Command Handler by implementing `ICommandHandler<AggregateType, CommandType>` or `ICommandHandlerAsync<AggregateType, CommandType>` if you want to use async command handler.
- YES. You can use for creating new aggregate. If you want to check aggregate have not created yet, you should pass `ReferenceVersion` for 0.
- YES. You can use for existing aggregate.
- YES. Command executor run Version Validation. You need to pass `ReferenceVersion` that you refer. If current version is different with ReferenceVersion, it will throw `SekibanCommandInconsistentVersionException`.
- YES. Command executor load current aggregate and pass it to context. You can access with `context.GetState()` to access current AggregateState. When there is no event exists yet, `context.GetState().IsNew` will return true.
- NO. It does not have Tenant feature, but you can manually return your Root Partition Key `GetRootPartitionKey()`.

### `ICommandWithVersionValidationForExistingAggregate<AggregateType>`

- You need to return `GetAggregateId` your AggregateId (Guid). You need to return AggregateId that you already save at least one event. e.g.) `public Guid GetAggregateId() => ClientId; `
- You can declare Command Handler by implementing `ICommandHandler<AggregateType, CommandType>` or `ICommandHandlerAsync<AggregateType, CommandType>` if you want to use async command handler.
- NO. You can NOT use for creating new aggregate. It will throw `SekibanAggregateNotExistsException`
- YES. You can use for existing aggregate.
- YES. Command executor run Version Validation. You need to pass `ReferenceVersion` that you refer. If current version is different with ReferenceVersion, it will throw `SekibanCommandInconsistentVersionException`.
- YES. Command executor load current aggregate and pass it to context. You can access with `context.GetState()` to access current AggregateState. When there is no event exists yet, command handler will not be called.
- NO. It does not have Tenant feature, but you can manually return your Root Partition Key `GetRootPartitionKey()`.

### `ICommandWithoutLoadingAggregate<AggregateType>`

- You need to return `GetAggregateId` your AggregateId (Guid). You need to return AggregateId that you already save at least one event. e.g.) `public Guid GetAggregateId() => ClientId; `
- You can declare Command Handler by implementing `ICommandWithoutLoadingAggregateHandler<AggregateType, CommandType>` or `ICommandWithoutLoadingAggregateHandlerAsync<AggregateType, CommandType>` if you want to use async command handler.
- YES. You can use for creating new aggregate.
- YES. You can use for existing aggregate.
- NO. Command executor does not run Version Validation.
- NO. Command executor will not load current aggregate and pass it to context. You can it in command handler, but this type of command is design for just create events without loading an aggregate. It will be faster to create event with this command type, although you can not check current state.
- NO. It does not have Tenant feature, but you can manually return your Root Partition Key `GetRootPartitionKey()`.

### `ITenantCommand<AggregateType>`

- You need to return `GetAggregateId` your AggregateId (Guid). You can return `Guid.NewGuid()` or Guid that is not exist in same aggregates to create new aggregate.
- You can declare Command Handler by implementing `ICommandHandler<AggregateType, CommandType>` or `ICommandHandlerAsync<AggregateType, CommandType>` if you want to use async command handler.
- YES. You can use for creating new aggregate.
- YES. You can use for existing aggregate.
- NO. Command executor does not run Version Validation.
- YES. Command executor load current aggregate and pass it to context. You can access with `context.GetState()` to access current AggregateState. When there is no event exists yet, `context.GetState().IsNew` will return true.
- YES. It have Tenant feature, you can set TenantId and it will be the Root Partition Key.

### `ITenantCommandForExistingAggregate<AggregateType>`

- You need to return `GetAggregateId` your AggregateId (Guid). You need to return AggregateId that you already save at least one event. e.g.) `public Guid GetAggregateId() => ClientId; `
- You can declare Command Handler by implementing `ICommandHandler<AggregateType, CommandType>` or `ICommandHandlerAsync<AggregateType, CommandType>` if you want to use async command handler.
- NO. You can NOT use for creating new aggregate. It will throw `SekibanAggregateNotExistsException`
- YES. You can use for existing aggregate.
- NO. Command executor does not run Version Validation.
- YES. Command executor load current aggregate and pass it to context. You can access with `context.GetState()` to access current AggregateState. When there is no event exists yet, command handler will not be called.
- YES. It have Tenant feature, you can set TenantId and it will be the Root Partition Key.

### `ITenantCommandWithVersionValidation<AggregateType>`

- You need to return `GetAggregateId` your AggregateId (Guid). You can return `Guid.NewGuid()` or Guid that is not exist in same aggregates to create new aggregate.
- You can declare Command Handler by implementing `ICommandHandler<AggregateType, CommandType>` or `ICommandHandlerAsync<AggregateType, CommandType>` if you want to use async command handler.
- YES. You can use for creating new aggregate. If you want to check aggregate have not created yet, you should pass `ReferenceVersion` for 0.
- YES. You can use for existing aggregate.
- YES. Command executor run Version Validation. You need to pass `ReferenceVersion` that you refer. If current version is different with ReferenceVersion, it will throw `SekibanCommandInconsistentVersionException`.
- YES. Command executor load current aggregate and pass it to context. You can access with `context.GetState()` to access current AggregateState. When there is no event exists yet, `context.GetState().IsNew` will return true.
- YES. It have Tenant feature, you can set TenantId and it will be the Root Partition Key.

### `ITenantCommandWithVersionValidationForExistingAggregate<AggregateType>`

- You need to return `GetAggregateId` your AggregateId (Guid). You need to return AggregateId that you already save at least one event. e.g.) `public Guid GetAggregateId() => ClientId; `
- You can declare Command Handler by implementing `ICommandHandler<AggregateType, CommandType>` or `ICommandHandlerAsync<AggregateType, CommandType>` if you want to use async command handler.
- NO. You can NOT use for creating new aggregate. It will throw `SekibanAggregateNotExistsException`
- YES. You can use for existing aggregate.
- YES. Command executor run Version Validation. You need to pass `ReferenceVersion` that you refer. If current version is different with ReferenceVersion, it will throw `SekibanCommandInconsistentVersionException`.
- YES. Command executor load current aggregate and pass it to context. You can access with `context.GetState()` to access current AggregateState. When there is no event exists yet, command handler will not be called.
- YES. It have Tenant feature, you can set TenantId and it will be the Root Partition Key.

### `ITenantCommandWithoutLoadingAggregate<AggregateType>`

- You need to return `GetAggregateId` your AggregateId (Guid). You need to return AggregateId that you already save at least one event. e.g.) `public Guid GetAggregateId() => ClientId; `
- You can declare Command Handler by implementing `ICommandWithoutLoadingAggregateHandler<AggregateType, CommandType>` or `ICommandWithoutLoadingAggregateHandlerAsync<AggregateType, CommandType>` if you want to use async command handler.
- YES. You can use for creating new aggregate.
- YES. You can use for existing aggregate.
- NO. Command executor does not run Version Validation.
- NO. Command executor will not load current aggregate and pass it to context. You can it in command handler, but this type of command is design for just create events without loading an aggregate. It will be faster to create event with this command type, although you can not check current state.
- YES. It have Tenant feature, you can set TenantId and it will be the Root Partition Key.

## Clean up command.

When you don't need to save command input for following reason, you can clean up your command.
- Contain Personal Information.
- Too long to save.

You can add interface `ICleanupNecessaryCommand<TCommand>` and implement  `CleanupCommand` method, your command executor will call `CleanupCommand` before it will save to items.

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
