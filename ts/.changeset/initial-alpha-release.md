---
"@sekiban/core": minor
"@sekiban/postgres": minor
"@sekiban/cosmos": minor
"@sekiban/dapr": minor
---

Initial alpha release of Sekiban TypeScript packages

- **@sekiban/core**: Core event sourcing and CQRS framework with schema-based type system
- **@sekiban/postgres**: PostgreSQL storage provider with full event store implementation
- **@sekiban/cosmos**: Azure Cosmos DB storage provider for globally distributed applications
- **@sekiban/dapr**: Dapr actor integration with automatic snapshot management

Features:
- Schema-based event and command definitions using Zod
- Type-safe command and query execution
- Multiple storage provider support
- Distributed systems ready with Dapr actors
- Comprehensive error handling with neverthrow
- Full TypeScript support with proper type inference