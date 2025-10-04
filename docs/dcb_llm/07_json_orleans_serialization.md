# Serialization & Domain Types

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md)
> - [MultiProjection](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Command Workflow](06_workflow.md)
> - [Serialization & Domain Types](07_json_orleans_serialization.md) (You are here)
> - [API Implementation](08_api_implementation.md)
> - [Client UI (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Storage Providers](11_dapr_setup.md)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

Serialization in DCB is explicit: you register every event, tag, projector, query, and state payload via
`DcbDomainTypes`. This removes reflection surprises and enables code generation for Orleans.

## DcbDomainTypes Catalog

`DcbDomainTypes` aggregates six registries plus shared `JsonSerializerOptions` (`src/Sekiban.Dcb/DcbDomainTypes.cs`).
Use the builder to register your domain types.

```csharp
public static DcbDomainTypes GetDomainTypes() =>
    DcbDomainTypes.Simple(types =>
    {
        types.EventTypes.RegisterEventType<StudentCreated>();
        types.TagStatePayloadTypes.RegisterPayloadType<StudentState>();
        types.TagProjectorTypes.RegisterProjector<StudentProjector>();
        types.TagTypes.RegisterTagGroupType<StudentTag>();
        types.MultiProjectorTypes.RegisterProjector<WeatherForecastProjection>();
        types.QueryTypes.RegisterListQuery<GetStudentListQuery>();
    });
```

### JSON Options

- Defaults to camelCase, non-indented output.
- Override via `DcbDomainTypes.Simple(builder => { ... }, jsonOptions: customOptions)`.
- Event stores rely on these options for serialization; keep them consistent across services.

## Orleans Serialization

DCB leverages Orleans Source Generators for tag state payloads and query results. Annotate records with
`[GenerateSerializer]` and add `[Id(n)]` attributes when necessary.

- Tag state payload example: `internalUsages/Dcb.Domain/Student/StudentState.cs`
- MultiProjection responses: `internalUsages/Dcb.Domain/Projections/WeatherForecastItem.cs`

For event payloads you can use either Orleans serialization or System.Text.Json; they are serialized by the event store.

`Sekiban.Dcb.Orleans` customizes Orleans serialization via
`NewtonsoftJsonDcbOrleansSerializer` for backward compatibility (`src/Sekiban.Dcb.Orleans/NewtonsoftJsonDcbOrleansSerializer.cs`).

## Event Metadata and Sortable IDs

Events are wrapped in `SerializableEvent` before persistence. Payloads are serialized bytes accompanied by the payload
name so the runtime can deserialize without dynamic type discovery (`tasks/dcb.design/records.md`).

`SortableUniqueId` encodes timestamp + entropy to preserve order even across distributed nodes
(`src/Sekiban.Dcb/Common/SortableUniqueId.cs`). Use the helpers when generating ids.

## Tag Identification

Tags serialize as strings `"Group:Content"`. Implement `ITag` or convenience interfaces to ensure reversible
serialization. For hierarchical tags include separators in the content (e.g., `tenant/customerId`).

## Custom JSON Contexts

If you need advanced converters (e.g., for value objects), register them in the shared `JsonSerializerOptions` passed to
`DcbDomainTypes`. The executor reuses those options when serializing commands for logging and when events travel through
the publisher.

## Versioning Strategy

- **ProjectorVersion** – bump when tag projector logic changes; forces cache invalidation.
- **MultiProjectorVersion** – bump when the read model schema changes; grains rebuild from scratch.
- **Json Contracts** – version query results at the type level (e.g., `WeatherForecastCountResultV2`).

Keep backward compatibility in mind—older Blazor clients may call the API during rollout.

## Troubleshooting

- Missing type registration yields runtime errors like "Event type not registered" when executing commands.
- JSON mismatches manifest as deserialization failures in event store backends. Log the payload name from `EventMetadata`
  to track down the offending type.
- Orleans may require a full rebuild if new `[GenerateSerializer]` types were added.
