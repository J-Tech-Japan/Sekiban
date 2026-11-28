# Sekiban DCB 11.0.1-preview03

- バージョン: `11.0.1-preview03`。DCB 11 系の 3 つめのプレビューです。DCB ライブラリは 26 パッケージです。対象は net10.0 と net11.0 です。
- 依存パッケージの更新だけです。net11.0 の PostgreSQL のパッケージは Entity Framework Core と Npgsql の 11.0.0 正式版を使うようになりました。WithResult のパッケージは、net11.0 向けのビルドを含む `ResultBoxes` 0.4.1 を使います。公開 API は 11.0.1-preview02 から変わっていません。
- DCB のイベントデータの移行は不要です。

## 依存パッケージ

- **net11.0 の PostgreSQL。** `Sekiban.Dcb.Postgres` は Entity Framework Core と `Microsoft.EntityFrameworkCore.Relational` の `11.0.0`、`Npgsql.EntityFrameworkCore.PostgreSQL` の `11.0.0` を参照します（preview01 と preview02 はリリース候補の `11.0.0-rc.3` でした）。`Sekiban.Dcb.MaterializedView.Postgres` は `Npgsql` `11.0.0` を参照します。EF Core と Npgsql EF Core の 11.0.0 は net11.0 向けのビルドしかないため、net10.0 は 10.x のままです。
- **net10.0** の依存パッケージのバージョンは DCB 10.25.x と同じです。
- **ResultBoxes 0.4.1。** WithResult の 4 パッケージ（`Sekiban.Dcb.WithResult`、`Sekiban.Dcb.WithResult.Model`、`Sekiban.Dcb.Orleans.WithResult`、`Sekiban.Dcb.WithResult.Testing`）は、`ResultBoxes` 0.4.0 に代わって `0.4.1` に依存します。0.4.1 は net11.0 向けのビルドを追加したもので、API は 0.4.0 と同じです。ほかの DCB パッケージは引き続き `ResultBoxes` に依存しません。
- `Microsoft.Orleans` は、どちらの対象でも 10.4.2 のままです。

## 11.0.1-preview02 からの更新

- API の変更もデータの移行もありません。パッケージのバージョンを更新してください。
- WithResult のパッケージと一緒に `ResultBoxes` 0.4.0 を直接参照している場合は、その参照を 0.4.1 に上げてください。そのままだと NuGet がパッケージのダウングレード（NU1605）を報告します。
- net11.0 で PostgreSQL を使う場合、ご自分で参照している Entity Framework Core や Npgsql EF Core は 11.0.0 以降にしてください。

## テンプレート

11 系の 5 つの DCB テンプレートは `Sekiban.Dcb` `11.0.1-preview03`（各テンプレートの `SekibanDcbTemplateVersion.props` の `SekibanDcbVersion`）を参照するようになりました。Orleans（WithResult）テンプレートは `ResultBoxes` 0.4.1 を参照します。これまでのプレビューと同じく、テンプレートのプレビューはタグ付け・リリースしません。
