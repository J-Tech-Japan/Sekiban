# Sekiban.Dcb.BlobStorage.S3

AWS S3 implementation of `IBlobStorageSnapshotAccessor` for offloading large MultiProjection snapshots in Sekiban DCB.

📚 **Full Documentation**: [sekiban.dev](https://www.sekiban.dev/)

## Sekiban Implementations

| Implementation | Status |
|---------------|--------|
| **Sekiban DCB** | ✅ Recommended |
| Sekiban.Pure | ⚠️ Deprecated |

## Installation

```bash
dotnet add package Sekiban.Dcb.BlobStorage.S3
```

## Usage

```csharp
// Program.cs
services.AddSekibanDcbS3BlobStorage(configuration);
```

### appsettings.json

```json
{
  "S3BlobStorage": {
    "BucketName": "sekiban-snapshots",
    "Prefix": "projections",
    "EnableEncryption": true,
    "Region": "ap-northeast-1"
  }
}
```

### LocalStack example

```json
{
  "S3BlobStorage": {
    "BucketName": "local-sekiban-snapshots",
    "ServiceUrl": "http://localhost:4566",
    "ForcePathStyle": true
  }
}
```

## Snapshot prefixes

In releases after `dcb-v10.22.0` (the fix is unreleased at the time of writing), the configured prefix applies to all newly written snapshot blobs. Seekable writes use `{prefix}/{projector}/{sha256}.bin` (row snapshots include the version in `{projector}`); null or empty prefixes keep the existing keys. Only trailing `/` characters are trimmed from the prefix. Non-seekable writes keep their existing prefixed GUID keys.

Deterministic keys from seekable writes since `df24f127` were un-prefixed. Those blobs stay at their old keys and remain readable without migration; non-seekable writes and writes before that commit could already be prefixed. Identical content is stored once more under the prefix after upgrading. Any listing or deletion by prefix must also account for old un-prefixed keys using `OffloadKeyEnumerator` and the referenced-key safety rules. When snapshot and cold-event storage are registered from the same options, they share the configured prefix. Cold-event paths are relative to their storage root, which can include a format scope in addition to the prefix; they are not always directly under `{prefix}/control/...` or `{prefix}/segments/...`.

Keep prefixes short, with no leading slash and forward slashes only; provider naming rules still apply. Prefixed deterministic keys over 512 characters throw `InvalidOperationException` before upload. A prefix of at most 57 characters keeps every projector name/version within PostgreSQL's valid bounds (256/128 characters) inside that limit. Null or empty prefixes introduce no length check.

Downloaded snapshots are retained in `localCacheDirectory` (default `<temp>/sekiban-snapshot-cache`); cache hits bypass storage, missing cache files are downloaded again, and only temporary download files are cleaned up, so size or clear the directory as distinct blobs accumulate. See [snapshot pruning and local read cache](../../../docs/dcb_llm/04_multiple_aggregate_projector.md#maintenance-window-snapshot-blob-pruning-1253-item-3).

## Related Packages

- `Sekiban.Dcb.DynamoDB` - DynamoDB event store for AWS deployments

## License

Apache 2.0 - Copyright (c) 2022- J-Tech Japan
