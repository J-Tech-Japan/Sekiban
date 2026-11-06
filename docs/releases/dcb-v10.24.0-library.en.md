# Sekiban DCB 10.24.0

- Version: `10.24.0`. 26 DCB library packages. Targets net9.0 and net10.0.
- `Microsoft.Orleans.*` moves from 10.3.1 to **10.4.2**. This is the only direct dependency change; the DCB library code and public API are unchanged since 10.23.4.
- No DCB event data migration is required.

## Orleans 10.4.2

- **Upgrade the whole Orleans cluster together.** Running Orleans 10.3.1 and 10.4.2 silos in one cluster is not supported. Align every direct `Microsoft.Orleans.*` reference in your hosts on 10.4.2.
- **Metrics changed in Orleans 10.4.0.** `orleans-app-requests-latency-bucket`, `-count` and `-sum` are replaced by the `orleans-app-requests-latency` histogram in fractional milliseconds, and `orleans-grains` uses the `grain_type` label instead of `type`. Update dashboards and alerts. See the [Orleans 10.4.0 release notes](https://github.com/dotnet/orleans/releases/tag/v10.4.0).
- **Your own grain interfaces.** Orleans 10.4.0 changed the wire IDs of RPC arguments for grain methods that put `[Id]` on parameters or have a `CancellationToken` that is not the last parameter. DCB's own grains do not do this. If yours do, upgrade every silo and every client together.
- **New transitive analyzers.** Orleans 10.4.2 adds `Microsoft.Orleans.Analyzers` and `Microsoft.Orleans.CodeGenerator` as dependencies of most of its packages, including Core, Runtime, Serialization and Streaming.AzureStorage, so projects that reference only Orleans serialization (for example domain projects) now run the Orleans generator and analyzers.
- **PostgreSQL databases created with Orleans ADO.NET scripts.** Orleans 10.4.2 changes the PostgreSQL Clustering and Persistence scripts. An existing database keeps working, because Orleans falls back to the installed cleanup query, but it does not get the new behaviour until you upgrade it. The `OrleansSchemaInitializer` in the AWS templates does not upgrade an existing database; do not force it to reinitialize, because that drops grain state and reminders. The non-destructive steps are in the deployment guide (`docs/dcb_llm/16_deployment.md`, "Orleans 10.4.2 PostgreSQL upgrade").

## Templates

Templates for 10.24.0, including the refreshed PostgreSQL scripts of the AWS templates, are released separately, after the libraries.
