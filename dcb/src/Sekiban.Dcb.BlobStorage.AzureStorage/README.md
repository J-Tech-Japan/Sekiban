# Sekiban.Dcb.BlobStorage.AzureStorage

Azure Blob Storage integration for Sekiban Dynamic Consistency Boundary (DCB) framework.

📚 **Full Documentation**: [sekiban.dev](https://www.sekiban.dev/)

## Sekiban Implementations

| Implementation | Status |
|---------------|--------|
| **Sekiban DCB** | ✅ Recommended |
| Sekiban.Pure | ⚠️ Deprecated |

## Overview

This package provides Azure Blob Storage-based snapshot offloading for Sekiban DCB MultiProjection. It enables efficient storage of large projection state snapshots in Azure Blob Storage, reducing memory pressure and improving scalability for projections with significant state.

## Features

- **Binary Snapshot Storage**: Efficiently stores projection snapshots as binary data in Azure Blob Storage
- **Automatic Container Management**: Automatically creates blob containers if they don't exist
- **Flexible Configuration**: Supports both connection string and BlobServiceClient-based initialization
- **Prefix Support**: Organize snapshots with custom prefixes for multi-tenant scenarios

## Installation

```bash
dotnet add package Sekiban.Dcb.BlobStorage.AzureStorage --version 1.0.2-preview03
```

## Usage

### Basic Setup with Connection String

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
{
    var connectionString = configuration["AzureStorage:ConnectionString"];
    return new AzureBlobStorageSnapshotAccessor(
        connectionString,
        "multiprojection-snapshots", // Container name
        "production"                  // Optional prefix
    );
});
```

### Setup with BlobServiceClient (Recommended for Aspire)

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
{
    var blobServiceClient = sp.GetRequiredService<BlobServiceClient>();
    return new AzureBlobStorageSnapshotAccessor(
        blobServiceClient,
        "multiprojection-snapshots"  // Container name
    );
});
```

### With Aspire and Keyed Services

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
{
    // Use Aspire-configured BlobServiceClient
    var blobServiceClient = sp.GetRequiredKeyedService<BlobServiceClient>("MultiProjectionOffload");
    return new AzureBlobStorageSnapshotAccessor(
        blobServiceClient,
        "multiprojection-snapshots"
    );
});
```

## Integration with Sekiban DCB Orleans

This package is designed to work seamlessly with Sekiban.Dcb.Orleans for snapshot offloading in MultiProjection grains:

```csharp
// In Orleans silo configuration
siloBuilder.ConfigureServices(services =>
{
    // Register the Azure Blob Storage snapshot accessor
    services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
    {
        var blobServiceClient = sp.GetRequiredKeyedService<BlobServiceClient>("MultiProjectionOffload");
        return new AzureBlobStorageSnapshotAccessor(blobServiceClient, "snapshots");
    });
});
```

## Configuration Options

### Container Names
- Default: `multiprojection-snapshots`
- Customize based on your application needs
- Containers are created automatically if they don't exist

### Prefixes
- Use prefixes to organize snapshots by environment, tenant, or feature
- Example: `production/`, `tenant-123/`, `feature-x/`

With the SEK-G120 fix in this source revision, the configured prefix applies to all newly written snapshot blobs. Seekable writes use `{prefix}/{projector}/{sha256}.bin` (row snapshots include the version in `{projector}`); null or empty prefixes keep the existing keys. Only trailing `/` characters are trimmed from the prefix. Non-seekable writes keep their existing prefixed GUID keys.

Deterministic keys from seekable writes since `df24f127` were un-prefixed. Those blobs stay at their old keys and remain readable without migration; non-seekable writes and writes before that commit could already be prefixed. Identical content is stored once more under the prefix after upgrading. Any listing or deletion by prefix must also account for old un-prefixed keys using `OffloadKeyEnumerator` and the referenced-key safety rules. Snapshot blobs now sit next to cold-event objects using the same configured prefix: `{prefix}/control/...` and `{prefix}/segments/...`.

Keep prefixes short, with no leading slash and forward slashes only; provider naming rules still apply. Prefixed deterministic keys over 512 characters throw `InvalidOperationException` before upload. A prefix of at most 57 characters keeps every projector name/version within PostgreSQL's valid bounds (256/128 characters) inside that limit. Null or empty prefixes introduce no length check.

This describes the source revision containing SEK-G120; the pinned installation example above does not identify a release containing the fix.

## Requirements

- .NET 9.0 or later
- Azure Storage Account (or Azurite for local development)
- Sekiban.Dcb package

## Dependencies

- Azure.Storage.Blobs (12.22.2)
- Sekiban.Dcb (1.0.2-preview03)

## License

Apache-2.0

## Support

For issues, questions, or contributions, please visit:
https://github.com/J-Tech-Japan/Sekiban