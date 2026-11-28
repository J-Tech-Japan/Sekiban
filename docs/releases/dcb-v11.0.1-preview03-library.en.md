# Sekiban DCB 11.0.1-preview03

- Version: `11.0.1-preview03`. The third preview of the DCB 11 line. 26 DCB library packages. Targets net10.0 and net11.0.
- Dependency update only: on net11.0 the PostgreSQL packages now use the Entity Framework Core and Npgsql 11.0.0 GA releases, and the WithResult packages use `ResultBoxes` 0.4.1, which ships a net11.0 build. The public API is unchanged since 11.0.1-preview02.
- No DCB event data migration is required.

## Dependencies

- **net11.0, PostgreSQL.** `Sekiban.Dcb.Postgres` references Entity Framework Core and `Microsoft.EntityFrameworkCore.Relational` `11.0.0` and `Npgsql.EntityFrameworkCore.PostgreSQL` `11.0.0` (preview01 and preview02 used the `11.0.0-rc.3` release candidates). `Sekiban.Dcb.MaterializedView.Postgres` references `Npgsql` `11.0.0`. These EF Core and Npgsql EF Core 11.0.0 packages ship only a net11.0 build, so net10.0 keeps its 10.x versions.
- **net10.0** keeps the same dependency versions as DCB 10.25.x.
- **ResultBoxes 0.4.1.** The four WithResult packages (`Sekiban.Dcb.WithResult`, `Sekiban.Dcb.WithResult.Model`, `Sekiban.Dcb.Orleans.WithResult`, `Sekiban.Dcb.WithResult.Testing`) now depend on `ResultBoxes` `0.4.1` instead of 0.4.0. 0.4.1 adds a net11.0 build; its API is the same as 0.4.0. Other DCB packages still do not depend on `ResultBoxes`.
- `Microsoft.Orleans` stays at 10.4.2 on both target frameworks.

## Upgrading from 11.0.1-preview02

- No API change and no data migration: update the package versions.
- If your project references `ResultBoxes` 0.4.0 directly together with a WithResult package, update that reference to 0.4.1. Otherwise NuGet reports a package downgrade (NU1605).
- On net11.0 with PostgreSQL, your own references to Entity Framework Core or Npgsql EF Core must be 11.0.0 or later.

## Templates

The five DCB templates on the 11 line now reference `Sekiban.Dcb` `11.0.1-preview03` (`SekibanDcbVersion` in each template's `SekibanDcbTemplateVersion.props`), and the Orleans (WithResult) template references `ResultBoxes` 0.4.1. As with earlier previews, template previews are not tagged or released.
