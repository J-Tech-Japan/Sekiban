# Sekiban DCB 10.24.1

- Version: `10.24.1`. 26 DCB library packages. Targets net9.0 and net10.0.
- A dependency security update. The DCB library code and public API are unchanged since 10.24.0, and Orleans stays at 10.4.2.
- No DCB event data migration is required.

## Security fixes

- `Sekiban.Dcb.CosmosDb` brought `Microsoft.AspNetCore.WebUtilities` 2.3.0 (CVE-2026-118750). It now references `Microsoft.AspNetCore.Http` and `Microsoft.AspNetCore.Http.Abstractions` 2.3.14 and `Microsoft.AspNetCore.WebUtilities` 2.3.14. `Sekiban.Dcb.Core` also moves `Microsoft.AspNetCore.Http.Abstractions` from 2.3.9 to 2.3.14.
- `Sekiban.Dcb.Sqlite`, `Sekiban.Dcb.ColdStorage` and `Sekiban.Dcb.MaterializedView.Sqlite` brought `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (GHSA-2m69-gcr7-jv3q). They now reference `Microsoft.Data.Sqlite` 10.0.15, which brings SQLitePCLRaw 2.1.13.
- `Sekiban.Dcb.MaterializedView.SqlServer` brought `System.Security.Cryptography.Pkcs` 9.0.4 on net9.0 (CVE-2026-84008). It now references `Microsoft.Data.SqlClient` 6.1.8.
- `Sekiban.Dcb.MaterializedView.MySql` referenced `MySqlConnector` 2.4.0 (GHSA-473q-m89c-ghf8). It now references 2.6.2.

## Other dependency updates

- `Microsoft.Extensions.*`, EF Core and `Microsoft.Data.Sqlite` move to 10.0.15 (from 10.0.3 and 10.0.5). The net9.0-only EF Core references of `Sekiban.Dcb.Postgres` move from 9.0.13 to 9.0.23. These are the .NET security releases of 2026-11-10.
- `Sekiban.Dcb.MaterializedView.Postgres` references `Npgsql` 10.0.3 for net10.0 and 9.0.5 for net9.0. `Sekiban.Dcb.Postgres` references `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 for net10.0; its net9.0 reference stays 9.0.4.
- If your project references lower versions of these packages directly, restore fails with NU1605 (package downgrade). Raise those references to at least the versions above.

## Templates

Templates for 10.24.1 are released separately, after the libraries.
