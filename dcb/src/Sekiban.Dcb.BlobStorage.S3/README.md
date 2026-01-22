# Sekiban.Dcb.BlobStorage.S3

AWS S3 implementation of `IBlobStorageSnapshotAccessor` for offloading large MultiProjection snapshots.

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

