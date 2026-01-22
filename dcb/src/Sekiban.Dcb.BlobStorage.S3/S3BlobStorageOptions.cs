namespace Sekiban.Dcb.BlobStorage.S3;

/// <summary>
///     Configuration options for S3 snapshot storage.
/// </summary>
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
    public bool ForcePathStyle { get; set; }
}
