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