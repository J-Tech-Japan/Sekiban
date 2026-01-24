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

## Related Packages

- `Sekiban.Dcb.DynamoDB` - DynamoDB event store for AWS deployments

## License

Apache 2.0 - Copyright (c) 2022- J-Tech Japan
