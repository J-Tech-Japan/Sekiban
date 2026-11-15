# Sekiban Pure - Aggregate-based Event Sourcing

<!-- SEKIBAN_PURE_POLICY_START -->
> **Sekiban.Pure support policy / サポート方針**
>
> Sekiban.Pure is in maintenance mode: no new features. Its main target is .NET 10, and it builds for .NET 10 and .NET 11 (Sekiban.Pure 9.x remains available for .NET 9). We apply security fixes only: fixes for vulnerabilities in Sekiban.Pure itself and dependency updates that resolve published security advisories. For new projects we recommend Sekiban DCB.
>
> Sekiban.Pure はメンテナンスモードです（機能追加なし）。メインの対応は .NET 10 で、.NET 10 と .NET 11 向けにビルドします（.NET 9 向けには Sekiban.Pure 9.x が引き続き利用できます）。対応するのはセキュリティ修正のみです（Sekiban.Pure 自身の脆弱性の修正と、公開済みのセキュリティ勧告を解消する依存関係の更新）。新規プロジェクトには Sekiban DCB を推奨します。
<!-- SEKIBAN_PURE_POLICY_END -->


**Sekiban Pure** provides traditional aggregate-based event sourcing with Microsoft Orleans or Dapr for actor model support.

📚 **Documentation**: [sekiban.dev](https://www.sekiban.dev/)

## Sekiban Implementations

| Implementation | Description | Status |
|---------------|-------------|--------|
| **Sekiban DCB** | Dynamic Consistency Boundary - tag-based event sourcing | ✅ Recommended |
| Sekiban.Pure | Traditional aggregate-based event sourcing | 🛠️ Maintenance |

## Migration to DCB

New projects should use Sekiban DCB:

```bash
dotnet new install Sekiban.Dcb.Templates
dotnet new sekiban-dcb-orleans -n YourProjectName
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

## Community

Join the **J-Tech JAPAN OSS Discord** to ask questions and connect with other Sekiban users. There is a dedicated channel for the Sekiban community.

👉 [Join our Discord](https://discord.gg/kMdv978X)

## License

Apache 2.0 - Copyright (c) 2022- J-Tech Japan
