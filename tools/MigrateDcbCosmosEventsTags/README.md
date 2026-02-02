# Migrate DCB CosmosDB Events/Tags (Legacy -> /pk)

This tool exports legacy CosmosDB `events` and `tags` data to JSONL, deletes and recreates the containers with `/pk` partition key, then converts and uploads the data to the new format.

## Requirements
- Stop Orleans or any writers before running.
- Connection string with permissions to read/write and delete containers.

## Usage
```bash
dotnet run --project tools/MigrateDcbCosmosEventsTags -- \
  --connection-string "<cosmos-connection-string>" \
  --database "SekibanDcb" \
  --events-container "events" \
  --tags-container "tags" \
  --service-id "default" \
  --output-dir "./cosmos-backup" \
  --confirm
```

You can also set config values via `appsettings.json` or user-secrets:
```bash
dotnet user-secrets set "ConnectionStrings:SekibanDcbCosmos" "<cosmos-connection-string>" --project tools/MigrateDcbCosmosEventsTags
```

## What it does
1. Export all items from the legacy containers to JSONL files in `output-dir`.
2. Delete the legacy containers.
3. Recreate containers with partition key `/pk`.
4. Convert legacy items to the new schema and upload.

## Notes
- If `--confirm` is omitted, only the JSONL export runs (no delete/recreate/upload).
- Items missing `serviceId` will be assigned the provided `--service-id` (default `default`).
- `createdAt` and `timestamp` will fall back to `_ts` (Cosmos system timestamp) or `DateTime.UtcNow` when missing.
