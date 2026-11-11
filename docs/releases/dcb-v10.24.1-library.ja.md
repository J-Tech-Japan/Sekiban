# Sekiban DCB 10.24.1

- バージョン: `10.24.1`。DCB ライブラリは 26 パッケージです。net9.0 と net10.0 を対象とします。
- 依存パッケージのセキュリティ更新です。DCB ライブラリのコードと公開 API は 10.24.0 から変わっていません。Orleans は 10.4.2 のままです。
- DCB のイベントデータの移行は不要です。

## セキュリティの修正

- `Sekiban.Dcb.CosmosDb` には `Microsoft.AspNetCore.WebUtilities` 2.3.0（CVE-2026-118750）が入っていました。`Microsoft.AspNetCore.Http` と `Microsoft.AspNetCore.Http.Abstractions` 2.3.14、`Microsoft.AspNetCore.WebUtilities` 2.3.14 を参照するようにしました。`Sekiban.Dcb.Core` も、`Microsoft.AspNetCore.Http.Abstractions` を 2.3.9 から 2.3.14 に上げました。
- `Sekiban.Dcb.Sqlite`、`Sekiban.Dcb.ColdStorage`、`Sekiban.Dcb.MaterializedView.Sqlite` には `SQLitePCLRaw.lib.e_sqlite3` 2.1.11（GHSA-2m69-gcr7-jv3q）が入っていました。`Microsoft.Data.Sqlite` 10.0.15 を参照するようにしました。これで SQLitePCLRaw は 2.1.13 になります。
- `Sekiban.Dcb.MaterializedView.SqlServer` には、net9.0 で `System.Security.Cryptography.Pkcs` 9.0.4（CVE-2026-84008）が入っていました。`Microsoft.Data.SqlClient` 6.1.8 を参照するようにしました。
- `Sekiban.Dcb.MaterializedView.MySql` は `MySqlConnector` 2.4.0（GHSA-473q-m89c-ghf8）を参照していました。2.6.2 を参照するようにしました。

## そのほかの依存の更新

- `Microsoft.Extensions.*`、EF Core、`Microsoft.Data.Sqlite` を 10.0.15 に（10.0.3 と 10.0.5 から）上げました。`Sekiban.Dcb.Postgres` の net9.0 だけの EF Core の参照は、9.0.13 から 9.0.23 に上げました。2026-11-10 の .NET のセキュリティリリースです。
- `Sekiban.Dcb.MaterializedView.Postgres` は、`Npgsql` を net10.0 では 10.0.3、net9.0 では 9.0.5 で参照します。`Sekiban.Dcb.Postgres` は、`Npgsql.EntityFrameworkCore.PostgreSQL` を net10.0 では 10.0.3 で参照します。net9.0 の参照は 9.0.4 のままです。
- これらのパッケージを、より低いバージョンで直接参照しているプロジェクトでは、restore が NU1605（パッケージのダウングレード）で失敗します。その参照を上のバージョン以上に上げてください。

## テンプレート

10.24.1 のテンプレートは、ライブラリの後に、別途リリースします。
