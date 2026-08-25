# Sekiban Pure - Aggregate-based Event Sourcing

<!-- SEKIBAN_PURE_POLICY_START -->
> **Sekiban.Pure support policy / サポート方針**
>
> Sekiban.Pure is in maintenance mode (no new features; .NET 9). We plan to complete its lifecycle with .NET 9 (EOL Nov 10, 2026) — but if you run it in production, we will bring it to .NET 10/11 as a pure compatibility migration, no features (we shipped exactly this for Sekiban(Core) 0.25.0: unchanged public API, bidirectional data compatibility). Tell us in [#1169](https://github.com/J-Tech-Japan/Sekiban/issues/1169). For new projects we recommend Sekiban DCB.
>
> Sekiban.Pure は現在メンテナンスモードです（機能追加なし・.NET 9 対応）。.NET 9 の EOL（2026-11-10）でライフサイクルを完了する予定ですが、本番利用中の方がいれば .NET 10/11 への対応を機能追加なしの互換移行として行います（Sekiban(Core) 0.25.0 で同方式を実施済み: 公開 API 無変更・保存データ双方向互換）。必要な方は [#1169](https://github.com/J-Tech-Japan/Sekiban/issues/1169) でお知らせください。新規プロジェクトには Sekiban DCB を推奨します。
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
