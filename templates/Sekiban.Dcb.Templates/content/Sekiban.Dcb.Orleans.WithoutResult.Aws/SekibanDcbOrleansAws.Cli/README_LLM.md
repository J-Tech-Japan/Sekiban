# SekibanDcbOrleansAws CLI

This CLI reads the application's registered domain metadata. It does not connect to a database.

```bash
dotnet run --project SekibanDcbOrleansAws.Cli -- list
```

`list` prints multi-projector names and versions, tag-projector names and versions, and tag groups. Use `--help` for command help. No storage credentials are required. Database operations such as projection build, save, delete, status, tag export or cache sync are not implemented in this CLI.
