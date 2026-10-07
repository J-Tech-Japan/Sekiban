# SekibanDcbDecider

Start locally from the solution directory with Docker running:

```bash
dotnet build SekibanDcbDecider.slnx
dotnet run --project SekibanDcbDecider.AppHost --launch-profile http
```

Use the Aspire dashboard for the API, Blazor Web and database endpoints. The AppHost and project launch profiles set `ASPNETCORE_ENVIRONMENT=Development`. Stop the AppHost when finished. The AppHost configures the event store and the separate PostgreSQL MV target; `/api/mv/status`, `/api/mv/students`, `/api/mv/classrooms` and `/api/mv/enrollments` demonstrate the read model. `waitForSortableUniqueId` can wait for a command's position. `/api/weatherforecast-uwmv` demonstrates unsafe-window reads.

## Development settings

Debug event inspection and projection persist, deactivate, refresh, snapshot and overwrite-version routes exist only in Development. Any-origin CORS is limited to Development. Local streams use memory; they do not guarantee durable delivery on restart.

Sample users (including `admin@example.com`, password `Sekiban1234%`) and the fixed JWT signing key are Development-only. Identity tables and roles are initialized in every environment. Registration grants only User. The Blazor Web has no authentication client or login page; use WebNext for registration and login. The AppHost runs WebNext with `next dev`. Its server uses `NODE_ENV` to gate quick login and test-data controls and rejects test-data procedures outside development. Test-data API routes still require authentication in Development.

`Orleans:UseInMemoryStreams` selects memory streams locally. For deployed Azure Queue or Event Hub streams, supply the existing storage/queue configuration in the infrastructure guides; PubSubStore must be persistent.

## Before production

- Set and keep `Sekiban:ServiceId` stable. Its default is `sekiban-app`; it partitions events, projections, streams and materialized views. Use a distinct identity when applications share a backend. `Orleans:ServiceId` is a separate cluster setting. For a project generated earlier with data under `default`, retain its existing identity.
- Configure durable storage, clustering, stream delivery, snapshot access and allowed CORS origins for your deployment. Test restart recovery and your multi-silo topology.
- Keep debug and maintenance routes disabled. If operators need them, explicitly map them behind your own authenticated operator policy, for example `.RequireAuthorization("Operators")`; define the policy and its permissions before exposing routes.
- Grant only the database and blob permissions required by the configured stores, bootstrap schemas with an appropriate deployment identity, then review runtime schema permissions.
- Supply `Jwt__SecretKey` (at least 32 characters) from a secret store before deployment. Docker images use Production; Azure defaults to Production and AWS uses Staging/Production, so the development key is not loaded. The shipped infrastructure does not wire this secret: use an App Service Key Vault reference, a Container Apps secret reference or an ECS task definition `secrets` entry backed by Secrets Manager. Startup fails without a valid key.
- To create the first administrator outside Development, supply `Auth__InitialAdmin__Email` and `Auth__InitialAdmin__Password` from secrets when no administrator exists. An existing registered account is never promoted. Omit these settings after bootstrap. Build WebNext with `npm install` and `npm run build`; production startup uses `npm start` and `NODE_ENV=production`.
- Configure the bundled CLI with the same `Sekiban:ServiceId` (environment variable `Sekiban__ServiceId` or CLI user secrets) and store connection as the API. Its SQLite cache path includes the service identity.

## Design guidance

Read the upstream guides before changing defaults:

- [Immutable projection inputs](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/04_multiple_aggregate_projector.md): return new payloads; use `IMutatesProjectionInput` only as a deliberate optimization. Retryable catch-up and rebuild failures return 503 with `Retry-After`; bounded first-query waiting stays off.
- [Snapshot storage and blob prefixes](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/11_storage_providers.md): this template uses a dedicated snapshot container or bucket without a prefix; plan isolation when sharing storage.
- [Orleans and grain directory options](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/10_orleans_setup.md): distributed directory prerequisites must be met before enabling it.
- [Fast and strict consistency](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/04_multiple_aggregate_projector.md): choose the query guarantee deliberately.
- [Storage and schema permissions](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/11_storage_providers.md) and [materialized views](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/20_materialized_view.md).
