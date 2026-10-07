# SekibanDcbOrleansAws

Start locally from the solution directory with Docker running:

```bash
dotnet build SekibanDcbOrleansAws.slnx
dotnet run --project SekibanDcbOrleansAws.AppHost --launch-profile http
```

Use the Aspire dashboard for the API, Blazor Web and database endpoints. The AppHost and project launch profiles set `ASPNETCORE_ENVIRONMENT=Development`. Stop the AppHost when finished. The AppHost configures the event store and the separate PostgreSQL MV target; `/api/mv/status`, `/api/mv/students`, `/api/mv/classrooms` and `/api/mv/enrollments` demonstrate the read model. `waitForSortableUniqueId` can wait for a command's position. `/api/weatherforecast-uwmv` demonstrates unsafe-window reads.

## Development settings

Debug event inspection and projection persist, deactivate, refresh, snapshot and overwrite-version routes exist only in Development. Any-origin CORS is limited to Development. Local streams use memory; they do not guarantee durable delivery on restart.

The AppHost deliberately pins the LocalStack image in the AppHost to a release that starts without a license token. Review licensing requirements before upgrading. DynamoDB is the only event store supported by this AWS template. The shipped cloud stack also uses memory streams and grain storage; see [infrastructure](infrastructure/README.md) for restart limitations and the role of RDS.

## Before production

- Set and keep `Sekiban:ServiceId` stable. Its default is `sekiban-app`; it partitions events, projections, streams and materialized views. Use a distinct identity when applications share a backend. `Orleans:ServiceId` is a separate cluster setting. For a project generated earlier with data under `default`, retain its existing identity.
- Configure durable storage, clustering, stream delivery, snapshot access and allowed CORS origins for your deployment. Test restart recovery and your multi-silo topology.
- Keep debug and maintenance routes disabled. If operators need them, explicitly map them behind your own authenticated operator policy, for example `.RequireAuthorization("Operators")`; define the policy and its permissions before exposing routes.
- Grant only the database and blob permissions required by the configured stores, bootstrap schemas with an appropriate deployment identity, then review runtime schema permissions.

## Design guidance

Read the upstream guides before changing defaults:

- [Immutable projection inputs](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/04_multiple_aggregate_projector.md): return new payloads; use `IMutatesProjectionInput` only as a deliberate optimization. Retryable catch-up and rebuild failures return 503 with `Retry-After`; bounded first-query waiting stays off.
- [Snapshot storage and blob prefixes](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/11_storage_providers.md): this template uses a dedicated snapshot container or bucket without a prefix; plan isolation when sharing storage.
- [Orleans and grain directory options](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/10_orleans_setup.md): distributed directory prerequisites must be met before enabling it.
- [Fast and strict consistency](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/04_multiple_aggregate_projector.md): choose the query guarantee deliberately.
- [Storage and schema permissions](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/11_storage_providers.md) and [materialized views](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/20_materialized_view.md).
