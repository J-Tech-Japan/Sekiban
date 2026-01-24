# Sekiban Pure - Aggregate-based Event Sourcing

> ⚠️ **Deprecated**: Sekiban.Pure is deprecated. For new projects, use **Sekiban DCB** (Dynamic Consistency Boundary) instead.

**Sekiban Pure** provides traditional aggregate-based event sourcing with Microsoft Orleans or Dapr for actor model support.

📚 **Documentation**: [sekiban.dev](https://www.sekiban.dev/)

## Sekiban Implementations

| Implementation | Description | Status |
|---------------|-------------|--------|
| **Sekiban DCB** | Dynamic Consistency Boundary - tag-based event sourcing | ✅ Recommended |
| Sekiban.Pure | Traditional aggregate-based event sourcing | ⚠️ Deprecated |

## Migration to DCB

New projects should use Sekiban DCB:

```bash
dotnet new install Sekiban.Pure.Templates
dotnet new sekiban-orleans-aspire -n YourProjectName
```

## Pure Features

- **Aggregate-based**: Traditional DDD aggregate event sourcing
- **Orleans/Dapr**: Actor model integration
- **Multi-store**: Cosmos DB and PostgreSQL support

## Pure Packages

| Package | Description |
|---------|-------------|
| `Sekiban.Pure` | Core framework |
| `Sekiban.Pure.Orleans` | Orleans integration |
| `Sekiban.Pure.Dapr` | Dapr integration |
| `Sekiban.Pure.Postgres` | PostgreSQL event store |
| `Sekiban.Pure.CosmosDb` | Cosmos DB event store |
| `Sekiban.Pure.AspNetCore` | ASP.NET Core integration |
| `Sekiban.Pure.NUnit` | NUnit testing |

## Why DCB?

DCB (Dynamic Consistency Boundary) offers significant advantages over aggregate-based event sourcing:

- **Flexible boundaries**: Define consistency scope per command, not per aggregate
- **No saga complexity**: Cross-entity invariants without compensating events
- **Optimistic concurrency**: Tag-based conflict detection
- **Better scalability**: Actor model with dynamic tag placement

Learn more at [dcb.events](https://dcb.events)

## Documentation

- **Website**: [sekiban.dev](https://www.sekiban.dev/)
- **Pure Docs**: [docs/llm](https://github.com/J-Tech-Japan/Sekiban/tree/main/docs/llm) (EN) | [docs/llm_ja](https://github.com/J-Tech-Japan/Sekiban/tree/main/docs/llm_ja) (JP)
- **DCB Docs**: [docs/dcb_llm](https://github.com/J-Tech-Japan/Sekiban/tree/main/docs/dcb_llm) (EN) | [docs/dcb_llm_ja](https://github.com/J-Tech-Japan/Sekiban/tree/main/docs/dcb_llm_ja) (JP)

## License

Apache 2.0 - Copyright (c) 2022- J-Tech Japan
