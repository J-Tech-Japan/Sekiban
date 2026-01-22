# Sekiban.Dcb.BlobStorage.S3 Design Document

## Overview

This document specifies the design for `Sekiban.Dcb.BlobStorage.S3`, an AWS S3 implementation of `IBlobStorageSnapshotAccessor` for offloading large MultiProjection state snapshots.

This package is **required** alongside `Sekiban.Dcb.DynamoDB` because DynamoDB has a 400KB item size limit, and projection states can exceed this limit.

---

## 1. Project Structure

```
dcb/src/Sekiban.Dcb.BlobStorage.S3/
├── S3BlobStorageSnapshotAccessor.cs     # IBlobStorageSnapshotAccessor implementation
├── S3BlobStorageOptions.cs              # Configuration options
├── SekibanDcbS3Extensions.cs            # DI registration extensions
├── README.md                            # Package documentation
└── Sekiban.Dcb.BlobStorage.S3.csproj    # Project file
```

---

## 2. Interface Implementation

### 2.1 Existing Interface (from Sekiban.Dcb.Core)

```csharp
public interface IBlobStorageSnapshotAccessor
{
    string ProviderName { get; }
    Task<string> WriteAsync(byte[] data, string projectorName, CancellationToken cancellationToken = default);
    Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default);
}
```

### 2.2 S3 Implementation

```csharp
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.BlobStorage.S3;

/// <summary>
///     AWS S3 implementation of IBlobStorageSnapshotAccessor.
///     Includes SDK-level retry configuration for resilience during transient failures.
/// </summary>
public sealed class S3BlobStorageSnapshotAccessor : IBlobStorageSnapshotAccessor
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _prefix;
    private readonly ILogger<S3BlobStorageSnapshotAccessor> _logger;

    public string ProviderName => "AwsS3";

    public S3BlobStorageSnapshotAccessor(
        IAmazonS3 s3Client,
        string bucketName,
        string? prefix = null,
        ILogger<S3BlobStorageSnapshotAccessor>? logger = null)
    {
        _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
        _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
        _prefix = prefix ?? string.Empty;
        _logger = logger ?? NullLogger<S3BlobStorageSnapshotAccessor>.Instance;
    }

    public S3BlobStorageSnapshotAccessor(
        string bucketName,
        string? prefix = null,
        ILogger<S3BlobStorageSnapshotAccessor>? logger = null)
    {
        _s3Client = new AmazonS3Client();
        _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
        _prefix = prefix ?? string.Empty;
        _logger = logger ?? NullLogger<S3BlobStorageSnapshotAccessor>.Instance;
    }

    public async Task<string> WriteAsync(
        byte[] data,
        string projectorName,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(projectorName, Guid.NewGuid().ToString("N"));

        using var ms = new MemoryStream(data, writable: false);
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = ms,
            ContentType = "application/octet-stream",
            // Optional: Server-side encryption
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        };

        await _s3Client.PutObjectAsync(request, cancellationToken);
        _logger.LogDebug("S3 write succeeded: {Bucket}/{Key}, Size: {Size} bytes", _bucketName, key, data.Length);
        return key;
    }

    public async Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            using var response = await _s3Client.GetObjectAsync(request, cancellationToken);
            using var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms, cancellationToken);
            var data = ms.ToArray();
            _logger.LogDebug("S3 read succeeded: {Bucket}/{Key}, Size: {Size} bytes", _bucketName, key, data.Length);
            return data;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3 read failed: {Bucket}/{Key}, StatusCode: {StatusCode}", _bucketName, key, ex.StatusCode);
            throw;
        }
    }

    private string BuildKey(string projectorName, string name)
    {
        var folder = string.IsNullOrEmpty(_prefix) ? projectorName : $"{_prefix.TrimEnd('/')}/{projectorName}";
        return $"{folder}/{name}.bin";
    }
}
```

---

## 3. Configuration Options

```csharp
namespace Sekiban.Dcb.BlobStorage.S3;

public class S3BlobStorageOptions
{
    /// <summary>
    ///     S3 bucket name for storing snapshot data.
    /// </summary>
    public string BucketName { get; set; } = "sekiban-snapshots";

    /// <summary>
    ///     Optional prefix (folder path) within the bucket.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    ///     AWS region (if not using default credential chain).
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    ///     Enable server-side encryption (AES256 by default).
    /// </summary>
    public bool EnableEncryption { get; set; } = true;

    /// <summary>
    ///     Custom service URL (for LocalStack or MinIO testing).
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    ///     Force path-style addressing (required for LocalStack/MinIO).
    /// </summary>
    public bool ForcePathStyle { get; set; } = false;
}
```

---

## 4. DI Registration

```csharp
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.BlobStorage.S3;

public static class SekibanDcbS3Extensions
{
    /// <summary>
    ///     Adds S3 blob storage accessor for snapshot offloading.
    /// </summary>
    public static IServiceCollection AddSekibanDcbS3BlobStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<S3BlobStorageOptions>(configuration.GetSection("S3BlobStorage"));

        services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<S3BlobStorageOptions>>().Value;
            var logger = sp.GetService<ILogger<S3BlobStorageSnapshotAccessor>>();

            // Try to get Aspire-injected or pre-configured IAmazonS3
            var s3Client = sp.GetService<IAmazonS3>();
            if (s3Client != null)
            {
                return new S3BlobStorageSnapshotAccessor(s3Client, options.BucketName, options.Prefix, logger);
            }

            // Create client from options
            var config = new AmazonS3Config();
            if (!string.IsNullOrEmpty(options.Region))
            {
                config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region);
            }
            if (!string.IsNullOrEmpty(options.ServiceUrl))
            {
                config.ServiceURL = options.ServiceUrl;
                config.ForcePathStyle = options.ForcePathStyle;
            }

            var client = new AmazonS3Client(config);
            return new S3BlobStorageSnapshotAccessor(client, options.BucketName, options.Prefix, logger);
        });

        return services;
    }

    /// <summary>
    ///     Adds S3 blob storage accessor with explicit bucket configuration.
    /// </summary>
    public static IServiceCollection AddSekibanDcbS3BlobStorage(
        this IServiceCollection services,
        string bucketName,
        string? prefix = null)
    {
        services.AddAWSService<IAmazonS3>();
        services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
        {
            var s3Client = sp.GetRequiredService<IAmazonS3>();
            var logger = sp.GetService<ILogger<S3BlobStorageSnapshotAccessor>>();
            return new S3BlobStorageSnapshotAccessor(s3Client, bucketName, prefix, logger);
        });
        return services;
    }

    /// <summary>
    ///     Adds S3 blob storage accessor using Aspire-provided S3 client.
    /// </summary>
    public static IServiceCollection AddSekibanDcbS3BlobStorageWithAspire(
        this IServiceCollection services,
        string bucketName,
        string? prefix = null)
    {
        services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
        {
            var s3Client = sp.GetRequiredService<IAmazonS3>();
            var logger = sp.GetService<ILogger<S3BlobStorageSnapshotAccessor>>();
            return new S3BlobStorageSnapshotAccessor(s3Client, bucketName, prefix, logger);
        });
        return services;
    }
}
```

---

## 5. Project File

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <LangVersion>preview</LangVersion>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <AssemblyName>Sekiban.Dcb.BlobStorage.S3</AssemblyName>
        <RootNamespace>Sekiban.Dcb.BlobStorage.S3</RootNamespace>
        <PackageId>Sekiban.Dcb.BlobStorage.S3</PackageId>
        <Authors>J-Tech Group</Authors>
        <Company>J-Tech-Japan</Company>
        <Copyright>Copyright (c) J-Tech-Japan 2025</Copyright>
        <PackageDescription>Sekiban - Dynamic Consistency Boundary AWS S3 Storage Integration</PackageDescription>
        <RepositoryUrl>https://github.com/J-Tech-Japan/Sekiban</RepositoryUrl>
        <PackageProjectUrl>https://github.com/J-Tech-Japan/Sekiban</PackageProjectUrl>
        <PackageVersion>10.0.2-preview03</PackageVersion>
        <Description>AWS S3 snapshot offloading for Sekiban DCB MultiProjection. Provides efficient binary snapshot storage for large projection states when using DynamoDB.</Description>
        <PackageTags>event-sourcing;cqrs;ddd;dcb;sekiban;aws;s3;dynamodb;snapshots</PackageTags>
        <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
        <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
        <PackageReadmeFile>README.md</PackageReadmeFile>
        <IncludeSymbols>true</IncludeSymbols>
        <SymbolPackageFormat>snupkg</SymbolPackageFormat>
        <GenerateSBOM>true</GenerateSBOM>
        <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Sekiban.Dcb.Core" Version="$(PackageVersion)" />
        <PackageReference Include="AWSSDK.S3" Version="3.*" />
        <PackageReference Include="AWSSDK.Extensions.NETCore.Setup" Version="3.*" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.2"/>
        <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.2"/>
        <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.2"/>
        <PackageReference Include="Microsoft.Sbom.Targets" Version="4.1.5">
            <PrivateAssets>all</PrivateAssets>
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        </PackageReference>
        <None Include="README.md" Pack="true" PackagePath="\"/>
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\Sekiban.Dcb.Core\Sekiban.Dcb.Core.csproj"/>
    </ItemGroup>

</Project>
```

---

## 6. Usage Configuration

### 6.1 appsettings.json

```json
{
  "AWS": {
    "Region": "ap-northeast-1"
  },
  "DynamoDb": {
    "EventsTableName": "SekibanEvents",
    "TagsTableName": "SekibanTags",
    "ProjectionStatesTableName": "SekibanProjectionStates"
  },
  "S3BlobStorage": {
    "BucketName": "my-sekiban-snapshots",
    "Prefix": "projections",
    "EnableEncryption": true
  }
}
```

### 6.2 Program.cs Registration

```csharp
// DynamoDB + S3 for offloading
services.AddSekibanDcbDynamoDb(configuration);
services.AddSekibanDcbS3BlobStorage(configuration);
```

### 6.3 Local Development with LocalStack

```json
{
  "S3BlobStorage": {
    "BucketName": "local-sekiban-snapshots",
    "ServiceUrl": "http://localhost:4566",
    "ForcePathStyle": true
  }
}
```

---

## 7. Integration with DynamoDB Package

The DynamoDB MultiProjectionStateStore should automatically use `IBlobStorageSnapshotAccessor` when available:

```csharp
public class DynamoMultiProjectionStateStore : IMultiProjectionStateStore
{
    private readonly IAmazonDynamoDB _client;
    private readonly DynamoDbEventStoreOptions _options;
    private readonly IBlobStorageSnapshotAccessor? _blobAccessor;

    // If state exceeds threshold, offload to S3
    private const int OffloadThresholdBytes = 350_000; // DynamoDB item limit is 400KB

    public async Task SaveStateAsync(MultiProjectionStateRecord state, CancellationToken ct)
    {
        var serialized = SerializeState(state);

        if (serialized.Length > OffloadThresholdBytes && _blobAccessor != null)
        {
            // Offload to S3
            var key = await _blobAccessor.WriteAsync(serialized, state.ProjectorName, ct);
            await SaveOffloadedStateAsync(state, key, ct);
        }
        else
        {
            // Store directly in DynamoDB
            await SaveDirectStateAsync(state, serialized, ct);
        }
    }

    public async Task<MultiProjectionStateRecord?> LoadStateAsync(string projectorName, CancellationToken ct)
    {
        var record = await LoadDynamoRecordAsync(projectorName, ct);
        if (record == null) return null;

        if (record.IsOffloaded && _blobAccessor != null)
        {
            // Load from S3
            var data = await _blobAccessor.ReadAsync(record.OffloadKey!, ct);
            return DeserializeState(data);
        }
        else
        {
            // Deserialize from DynamoDB record
            return DeserializeState(record.StateData);
        }
    }
}
```

---

## 8. Package Relationship

```
┌──────────────────────────────────────────────────────────┐
│                    Application                            │
├──────────────────────────────────────────────────────────┤
│  services.AddSekibanDcbDynamoDb(...)                     │
│  services.AddSekibanDcbS3BlobStorage(...)                │
└──────────────────────────────────────────────────────────┘
                           │
           ┌───────────────┼───────────────┐
           ▼               ▼               ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│ Sekiban.Dcb.    │ │ Sekiban.Dcb.    │ │ Sekiban.Dcb.    │
│ DynamoDB        │ │ BlobStorage.S3  │ │ Core            │
├─────────────────┤ ├─────────────────┤ ├─────────────────┤
│ IEventStore     │ │ IBlobStorage    │ │ Interfaces      │
│ IMultiProjection│ │ SnapshotAccessor│ │ Abstractions    │
│ StateStore      │ │                 │ │                 │
└─────────────────┘ └─────────────────┘ └─────────────────┘
         │                   │                   │
         ▼                   ▼                   │
    ┌─────────┐         ┌─────────┐             │
    │ DynamoDB│         │   S3    │             │
    └─────────┘         └─────────┘             │
                                                ▼
                                    (shared abstractions)
```

---

## 9. Testing Strategy

### 9.1 Unit Tests with Mocks

```csharp
[Fact]
public async Task WriteAsync_ShouldUploadToS3()
{
    // Arrange
    var mockS3 = new Mock<IAmazonS3>();
    var accessor = new S3BlobStorageSnapshotAccessor(mockS3.Object, "test-bucket");
    var data = new byte[] { 1, 2, 3, 4 };

    // Act
    var key = await accessor.WriteAsync(data, "TestProjector");

    // Assert
    mockS3.Verify(s => s.PutObjectAsync(
        It.Is<PutObjectRequest>(r => r.BucketName == "test-bucket"),
        It.IsAny<CancellationToken>()), Times.Once);
}
```

### 9.2 Integration Tests with LocalStack

```csharp
public class S3IntegrationTests : IAsyncLifetime
{
    private readonly AmazonS3Client _s3Client;
    private const string BucketName = "test-snapshots";

    public S3IntegrationTests()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = "http://localhost:4566",
            ForcePathStyle = true
        };
        _s3Client = new AmazonS3Client(config);
    }

    public async Task InitializeAsync()
    {
        await _s3Client.PutBucketAsync(BucketName);
    }

    public async Task DisposeAsync()
    {
        // Clean up bucket
    }

    [Fact]
    public async Task RoundTrip_ShouldPreserveData()
    {
        var accessor = new S3BlobStorageSnapshotAccessor(_s3Client, BucketName);
        var original = new byte[] { 1, 2, 3, 4, 5 };

        var key = await accessor.WriteAsync(original, "TestProjector");
        var retrieved = await accessor.ReadAsync(key);

        Assert.Equal(original, retrieved);
    }
}
```

---

## 10. Comparison with Azure Blob Storage Package

| Aspect | Azure Blob Storage | AWS S3 |
|--------|-------------------|--------|
| Package | `Sekiban.Dcb.BlobStorage.AzureStorage` | `Sekiban.Dcb.BlobStorage.S3` |
| SDK | `Azure.Storage.Blobs` | `AWSSDK.S3` |
| Container concept | Container | Bucket |
| Path separator | `/` | `/` |
| Encryption | Azure-managed | SSE-S3 / SSE-KMS |
| Local dev | Azurite | LocalStack / MinIO |
| Retry | Built-in BlobClientOptions | Built-in SDK config |

---

## 11. Summary

The `Sekiban.Dcb.BlobStorage.S3` package:

1. **Implements** `IBlobStorageSnapshotAccessor` interface from Sekiban.Dcb.Core
2. **Mirrors** the Azure Blob Storage package structure and patterns
3. **Required** for DynamoDB deployments due to 400KB item size limit
4. **Supports** both production AWS and local development (LocalStack/MinIO)
5. **Integrates** seamlessly with the DynamoDB MultiProjectionStateStore
